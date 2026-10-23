using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ProjectManagement.Services.SearchV2.Models;
using ProjectManagement.Services.SearchV2.Security;

namespace ProjectManagement.Services.SearchV2.Query;

public sealed record SearchCorrectionCandidate(
    string Token,
    int Frequency,
    int Authority,
    double TrigramSimilarity,
    double Score = 0);

public interface ISearchCorrectionService
{
    Task<string?> TryCorrectAsync(
        DbConnection connection,
        NormalizedSearchQuery query,
        SearchAccessContext access,
        CancellationToken cancellationToken);
}

/// <summary>
/// Provides conservative query assistance for zero/weak-result searches.
/// Vocabulary is drawn only from rows the current user may search and only from
/// titles plus high-authority typed terms (SearchTermKinds.Name, SearchTermKinds.Location,
/// SearchTermKinds.Organisation) as kept in "SearchVocabularyTerms". OCR,
/// narrative and arbitrary body text are deliberately excluded.
/// </summary>
public sealed partial class SearchCorrectionService : ISearchCorrectionService
{
    private readonly SearchV2Options _options;
    private readonly ISearchQueryNormalizer _normalizer;
    private readonly ILogger<SearchCorrectionService> _logger;

    public SearchCorrectionService(
        IOptions<SearchV2Options> options,
        ISearchQueryNormalizer normalizer,
        ILogger<SearchCorrectionService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> TryCorrectAsync(
        DbConnection connection,
        NormalizedSearchQuery query,
        SearchAccessContext access,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(access);

        var normalizedTokens = TokenRegex().Matches(query.Exact)
            .Select(match => match.Value)
            .Where(token => token.Length > 0)
            .ToArray();
        if (normalizedTokens.Length == 0 || normalizedTokens.Length > _options.CorrectionMaxTokens)
        {
            return null;
        }

        var originalTokens = TokenRegex().Matches(query.Original)
            .Select(match => match.Value)
            .ToArray();

        var eligible = new List<int>(normalizedTokens.Length);
        for (var index = 0; index < normalizedTokens.Length; index++)
        {
            var token = normalizedTokens[index];
            if (token.Length < _options.CorrectionMinTokenLength) continue;

            var originalToken = index < originalTokens.Length ? originalTokens[index] : token;
            if (SearchCorrectionScorer.IsProtectedOriginalToken(originalToken)) continue;
            eligible.Add(index);
        }

        if (eligible.Count == 0) return null;

        // One round trip for all eligible tokens, against a precomputed vocabulary.
        var candidatesByToken = await ReadCandidatesAsync(
            connection,
            eligible.Select(index => normalizedTokens[index]).Distinct(StringComparer.Ordinal).ToArray(),
            access,
            cancellationToken);

        var replacements = new Dictionary<int, string>();
        foreach (var index in eligible)
        {
            var token = normalizedTokens[index];
            if (!candidatesByToken.TryGetValue(token, out var candidates)) continue;
            if (candidates.Any(candidate => string.Equals(candidate.Token, token, StringComparison.OrdinalIgnoreCase)))
            {
                // The token is already present in the user's authorised vocabulary. A
                // zero-result multi-term query may simply be over-constrained; do not
                // "correct" a valid domain term in that situation.
                continue;
            }

            var best = SearchCorrectionScorer.SelectBest(token, candidates, _options);
            if (best is not null)
            {
                replacements[index] = best.Token;
            }
        }

        if (replacements.Count == 0) return null;

        var corrected = SearchCorrectionScorer.ApplyReplacements(normalizedTokens, replacements);
        corrected = _normalizer.NormalizeExact(corrected);
        if (string.IsNullOrWhiteSpace(corrected)
            || string.Equals(corrected, query.Exact, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        _logger.LogDebug(
            "Search V2 spelling assistance prepared a correction. TokenCount={TokenCount}, CorrectedTokenCount={CorrectedTokenCount}.",
            normalizedTokens.Length,
            replacements.Count);
        return corrected;
    }

    private async Task<IReadOnlyDictionary<string, IReadOnlyList<SearchCorrectionCandidate>>> ReadCandidatesAsync(
        DbConnection connection,
        string[] tokens,
        SearchAccessContext access,
        CancellationToken cancellationToken)
    {
        // "SearchVocabularyTerms" is maintained per SearchEntry by database triggers from the
        // title and the Name/Location/Organisation terms (authority 2/4/5/4). Only entries the
        // user may search contribute, so suggestions never reveal unauthorised wording.
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await SearchEngine.SetTrigramThresholdsAsync(
            connection,
            transaction,
            _options.CorrectionCandidateTrigramThreshold,
            _options.CorrectionCandidateTrigramThreshold,
            cancellationToken);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var authorization = SearchAuthorizationSql.Build(command, access);
        command.CommandText = $"""
            WITH state AS MATERIALIZED (
                SELECT "ActiveGeneration" AS generation
                FROM "SearchIndexState"
                WHERE "Id" = 1 AND "IndexVersion" = @indexVersion
            )
            SELECT q.token AS input, c.token, c.frequency, c.authority, c.trigram_similarity
            FROM unnest(@tokens) AS q(token)
            CROSS JOIN LATERAL (
                SELECT v."Token" AS token,
                       COUNT(DISTINCT v."SearchEntryId")::int AS frequency,
                       MAX(v."Authority")::int AS authority,
                       GREATEST(similarity(v."Token", q.token), word_similarity(q.token, v."Token"))::double precision AS trigram_similarity
                FROM "SearchVocabularyTerms" v
                JOIN "SearchEntries" e ON e."Id" = v."SearchEntryId"
                WHERE (v."Token" % q.token OR q.token <% v."Token")
                  AND LENGTH(v."Token") >= @minTokenLength
                  AND ABS(LENGTH(v."Token") - LENGTH(q.token)) <= @maxLengthDelta
                  AND e."Generation" = (SELECT generation FROM state)
                  AND e."IndexVersion" = @indexVersion
                  {authorization}
                GROUP BY v."Token"
                HAVING v."Token" = q.token
                    OR GREATEST(similarity(v."Token", q.token), word_similarity(q.token, v."Token")) >= @candidateThreshold
                ORDER BY
                    CASE WHEN v."Token" = q.token THEN 0 ELSE 1 END,
                    trigram_similarity DESC,
                    authority DESC,
                    frequency DESC,
                    ABS(LENGTH(v."Token") - LENGTH(q.token)),
                    v."Token"
                LIMIT @candidateLimit
            ) c;
            """;

        Add(command, "indexVersion", _options.ProjectionVersion);
        Add(command, "tokens", tokens);
        Add(command, "minTokenLength", _options.CorrectionMinTokenLength);
        Add(command, "maxLengthDelta", _options.CorrectionMaxLengthDelta);
        Add(command, "candidateThreshold", _options.CorrectionCandidateTrigramThreshold);
        Add(command, "candidateLimit", _options.CorrectionCandidateLimit);

        var candidates = new Dictionary<string, List<SearchCorrectionCandidate>>(StringComparer.Ordinal);
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var input = reader.GetString(0);
                if (!candidates.TryGetValue(input, out var list))
                {
                    list = new List<SearchCorrectionCandidate>(_options.CorrectionCandidateLimit);
                    candidates[input] = list;
                }

                list.Add(new SearchCorrectionCandidate(
                    reader.GetString(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetDouble(4)));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return candidates.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<SearchCorrectionCandidate>)pair.Value,
            StringComparer.Ordinal);
    }

    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = $"@{name}";
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    [GeneratedRegex("[\\p{L}\\p{N}][\\p{L}\\p{N}._/-]*", RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();
}

/// <summary>
/// Pure deterministic scoring used by the correction service and unit tests.
/// It intentionally favours authoritative/frequent PRISM terminology over a
/// superficially similar low-authority word.
/// </summary>
public static partial class SearchCorrectionScorer
{
    public static SearchCorrectionCandidate? SelectBest(
        string input,
        IEnumerable<SearchCorrectionCandidate> candidates,
        SearchV2Options options)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(input)) return null;

        var ranked = candidates
            .Where(candidate => !string.Equals(candidate.Token, input, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate with
            {
                Score = Score(input, candidate.Token, candidate.TrigramSimilarity, candidate.Frequency, candidate.Authority)
            })
            .Where(candidate => DamerauLevenshteinDistance(input, candidate.Token) <= options.CorrectionMaxEditDistance)
            .Where(candidate => candidate.Score >= options.CorrectionMinConfidence)
            .OrderByDescending(candidate => candidate.Score)
            .ThenByDescending(candidate => candidate.Authority)
            .ThenByDescending(candidate => candidate.Frequency)
            .ThenBy(candidate => DamerauLevenshteinDistance(input, candidate.Token))
            .ThenBy(candidate => candidate.Token, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return ranked;
    }

    public static double Score(
        string input,
        string candidate,
        double trigramSimilarity,
        int frequency,
        int authority)
    {
        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(candidate)) return 0;

        input = input.Trim().ToLowerInvariant();
        candidate = candidate.Trim().ToLowerInvariant();
        var maximumLength = Math.Max(input.Length, candidate.Length);
        if (maximumLength == 0) return 0;

        var distance = DamerauLevenshteinDistance(input, candidate);
        var editSimilarity = Math.Clamp(1d - ((double)distance / maximumLength), 0d, 1d);
        var lengthCompatibility = Math.Clamp(1d - ((double)Math.Abs(input.Length - candidate.Length) / maximumLength), 0d, 1d);
        var firstCharacterAgreement = input[0] == candidate[0] ? 1d : 0d;
        var frequencyScore = Math.Clamp(Math.Log2(Math.Max(1, frequency) + 1d) / 5d, 0d, 1d);
        var authorityScore = Math.Clamp(authority / 5d, 0d, 1d);

        return (editSimilarity * 0.45d)
             + (Math.Clamp(trigramSimilarity, 0d, 1d) * 0.20d)
             + (lengthCompatibility * 0.08d)
             + (firstCharacterAgreement * 0.05d)
             + (frequencyScore * 0.10d)
             + (authorityScore * 0.12d);
    }

    public static bool IsProtectedOriginalToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return true;
        token = token.Trim();

        if (token.Any(char.IsDigit)) return true;
        if (token.Length <= 2) return true;
        if (IdentifierLikeRegex().IsMatch(token)) return true;

        // Preserve deliberate all-caps military/project acronyms. A lower-case
        // natural-language typo such as "hydrbd" remains correction-eligible.
        var letters = token.Where(char.IsLetter).ToArray();
        return letters.Length is >= 2 and <= 8 && letters.All(char.IsUpper);
    }

    public static string ApplyReplacements(
        IReadOnlyList<string> normalizedTokens,
        IReadOnlyDictionary<int, string> replacements)
    {
        ArgumentNullException.ThrowIfNull(normalizedTokens);
        ArgumentNullException.ThrowIfNull(replacements);

        return string.Join(' ', normalizedTokens.Select((token, index) =>
            replacements.TryGetValue(index, out var replacement) && !string.IsNullOrWhiteSpace(replacement)
                ? replacement.Trim()
                : token));
    }

    public static int DamerauLevenshteinDistance(string source, string target)
    {
        source ??= string.Empty;
        target ??= string.Empty;
        if (source.Length == 0) return target.Length;
        if (target.Length == 0) return source.Length;

        var previousPrevious = new int[target.Length + 1];
        var previous = new int[target.Length + 1];
        var current = new int[target.Length + 1];
        for (var j = 0; j <= target.Length; j++) previous[j] = j;

        for (var i = 1; i <= source.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= target.Length; j++)
            {
                var substitutionCost = char.ToLowerInvariant(source[i - 1]) == char.ToLowerInvariant(target[j - 1]) ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(previous[j] + 1, current[j - 1] + 1),
                    previous[j - 1] + substitutionCost);

                if (i > 1 && j > 1
                    && char.ToLowerInvariant(source[i - 1]) == char.ToLowerInvariant(target[j - 2])
                    && char.ToLowerInvariant(source[i - 2]) == char.ToLowerInvariant(target[j - 1]))
                {
                    current[j] = Math.Min(current[j], previousPrevious[j - 2] + 1);
                }
            }

            var swap = previousPrevious;
            previousPrevious = previous;
            previous = current;
            current = swap;
        }

        return previous[target.Length];
    }

    [GeneratedRegex("^(?=.*[A-Za-z])(?=.*[0-9])[A-Za-z0-9._/-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierLikeRegex();
}
