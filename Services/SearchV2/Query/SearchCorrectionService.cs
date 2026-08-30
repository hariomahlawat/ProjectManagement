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
/// titles plus high-authority typed terms (Name, Location, Organisation). OCR,
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
        var replacements = new Dictionary<int, string>();

        for (var index = 0; index < normalizedTokens.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = normalizedTokens[index];
            if (token.Length < _options.CorrectionMinTokenLength) continue;

            var originalToken = index < originalTokens.Length ? originalTokens[index] : token;
            if (SearchCorrectionScorer.IsProtectedOriginalToken(originalToken)) continue;

            var candidates = await ReadCandidatesAsync(connection, token, access, cancellationToken);
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

    private async Task<IReadOnlyList<SearchCorrectionCandidate>> ReadCandidatesAsync(
        DbConnection connection,
        string token,
        SearchAccessContext access,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        var authorization = SearchAuthorizationSql.Build(command, access);
        command.CommandText = $"""
            WITH state AS (
                SELECT "ActiveGeneration"
                FROM "SearchIndexState"
                WHERE "Id" = 1 AND "IndexVersion" = @indexVersion
            ), authorised AS (
                SELECT e."Id", e."NormalizedTitle"
                FROM "SearchEntries" e
                JOIN state s ON s."ActiveGeneration" = e."Generation"
                WHERE e."IndexVersion" = @indexVersion
                  {authorization}
            ), vocabulary_rows AS (
                SELECT BTRIM(tokens.token) AS token, 2::int AS authority
                FROM authorised a
                CROSS JOIN LATERAL regexp_split_to_table(a."NormalizedTitle", '\s+') AS tokens(token)
                WHERE LENGTH(BTRIM(tokens.token)) >= @minTokenLength

                UNION ALL

                SELECT BTRIM(tokens.token) AS token,
                       CASE t."TermType"
                           WHEN @locationKind THEN 5
                           WHEN @nameKind THEN 4
                           WHEN @organisationKind THEN 4
                           ELSE 1
                       END::int AS authority
                FROM authorised a
                JOIN "SearchEntryTerms" t ON t."SearchEntryId" = a."Id"
                CROSS JOIN LATERAL regexp_split_to_table(t."NormalizedTerm", '\s+') AS tokens(token)
                WHERE t."TermType" IN (@nameKind, @locationKind, @organisationKind)
                  AND LENGTH(BTRIM(tokens.token)) >= @minTokenLength
            ), vocabulary AS (
                SELECT token,
                       COUNT(*)::int AS frequency,
                       MAX(authority)::int AS authority
                FROM vocabulary_rows
                WHERE token ~ '^[[:alpha:]][[:alpha:]-]*$'
                GROUP BY token
            ), candidate_scores AS (
                SELECT token,
                       frequency,
                       authority,
                       GREATEST(similarity(token, @token), word_similarity(@token, token))::double precision AS trigram_similarity
                FROM vocabulary
                WHERE ABS(LENGTH(token) - LENGTH(@token)) <= @maxLengthDelta
                  AND (
                      token = @token
                      OR LEFT(token, 1) = LEFT(@token, 1)
                      OR GREATEST(similarity(token, @token), word_similarity(@token, token)) >= @candidateThreshold
                  )
            )
            SELECT token, frequency, authority, trigram_similarity
            FROM candidate_scores
            WHERE token = @token OR trigram_similarity >= @candidateThreshold
            ORDER BY
                CASE WHEN token = @token THEN 0 ELSE 1 END,
                trigram_similarity DESC,
                authority DESC,
                frequency DESC,
                ABS(LENGTH(token) - LENGTH(@token)),
                token
            LIMIT @candidateLimit;
            """;

        Add(command, "indexVersion", _options.ProjectionVersion);
        Add(command, "token", token);
        Add(command, "minTokenLength", _options.CorrectionMinTokenLength);
        Add(command, "maxLengthDelta", _options.CorrectionMaxLengthDelta);
        Add(command, "candidateThreshold", _options.CorrectionCandidateTrigramThreshold);
        Add(command, "candidateLimit", _options.CorrectionCandidateLimit);
        Add(command, "nameKind", SearchTermKinds.Name);
        Add(command, "locationKind", SearchTermKinds.Location);
        Add(command, "organisationKind", SearchTermKinds.Organisation);

        var candidates = new List<SearchCorrectionCandidate>(_options.CorrectionCandidateLimit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            candidates.Add(new SearchCorrectionCandidate(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetDouble(3)));
        }

        return candidates;
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
