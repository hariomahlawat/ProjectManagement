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

        var tokenMatches = TokenRegex().Matches(query.Exact)
            .Where(match => match.Length > 0)
            .ToArray();
        var normalizedTokens = tokenMatches.Select(match => match.Value).ToArray();
        if (normalizedTokens.Length == 0 || normalizedTokens.Length > _options.CorrectionMaxTokens)
        {
            return null;
        }

        // Normalising splits "anti-tank" into two tokens, so the typed spelling of each token
        // (needed to keep acronyms such as ATGM) is found per typed word, not by position.
        // Against the searched text: an exclusion ("-drone") is not one of the tokens.
        var protectedTokens = SearchCorrectionScorer.ProtectedTokens(query.Searched, normalizedTokens, _normalizer.NormalizeExact, TokenRegex());

        var eligible = new List<int>(normalizedTokens.Length);
        for (var index = 0; index < normalizedTokens.Length; index++)
        {
            var token = normalizedTokens[index];
            if (token.Length < _options.CorrectionMinTokenLength) continue;
            if (protectedTokens[index]) continue;
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

        // Replace the corrected tokens in place, so joiners such as "&" in "ir&d" survive.
        var corrected = SearchCorrectionScorer.ApplyReplacements(
            query.Exact,
            tokenMatches.Select(match => (match.Index, match.Length)).ToArray(),
            replacements);
        corrected = _normalizer.NormalizeExact(corrected);
        if (string.IsNullOrWhiteSpace(corrected)
            || string.Equals(corrected, query.Exact, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Offer it as the person typed it: "Hydrabad Radar" → "Hyderabad Radar", not the
        // lower-case comparison form. Falls back to that form when the typed text cannot be
        // matched to the tokens.
        var typed = SearchCorrectionScorer.ReplaceInTypedText(query.Searched, normalizedTokens, replacements, _normalizer.NormalizeExact, TokenRegex());
        if (typed is not null && string.Equals(_normalizer.NormalizeExact(typed), corrected, StringComparison.Ordinal))
        {
            corrected = typed;
        }

        // The offered search keeps the exclusions: "hydrbad -drone" → "hyderabad -drone".
        if (query.ExcludedTyped.Count > 0)
        {
            corrected = corrected + " " + string.Join(' ', query.ExcludedTyped);
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
            cancellationToken,
            _options.QueryTimeoutSeconds);

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        var authorization = SearchAuthorizationSql.Build(command, access);
        command.CommandText = $"""
            WITH state AS MATERIALIZED (
                SELECT "ActiveGeneration" AS generation, "IndexVersion" AS version
                FROM "SearchIndexState"
                WHERE "Id" = 1 AND "IndexVersion" BETWEEN @minIndexVersion AND @indexVersion
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
                  AND e."IndexVersion" = (SELECT version FROM state)
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
        Add(command, "minIndexVersion", _options.MinimumServableProjectionVersion);
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

    /// <summary>
    /// For each normalised token, whether its typed spelling is protected from correction
    /// (an acronym, an identifier, a short or numeric token). Tokens are matched to the typed
    /// word they came from, and to their own spelling inside it: "anti-tank ATGM" protects
    /// "atgm" but not "anti" or "tank". A typed word with a digit is an identifier, and all of
    /// its parts stay protected ("atomz-123" is not offered as "atoms 123"). When the typed text cannot be aligned with the
    /// normalised tokens, every token is protected: no suggestion beats a wrong one.
    /// </summary>
    public static bool[] ProtectedTokens(
        string original,
        IReadOnlyList<string> normalizedTokens,
        Func<string, string> normalizeExact,
        Regex tokenPattern)
    {
        ArgumentNullException.ThrowIfNull(normalizedTokens);
        ArgumentNullException.ThrowIfNull(normalizeExact);
        ArgumentNullException.ThrowIfNull(tokenPattern);

        var tokens = TypedTokens(original, normalizedTokens, normalizeExact, tokenPattern);
        return tokens is null
            ? Enumerable.Repeat(true, normalizedTokens.Count).ToArray()
            : tokens.Select(token => token.WordHasDigit || IsProtectedOriginalToken(token.Typed)).ToArray();
    }

    /// <summary>
    /// Each normalised token's typed spelling and where it sits in <paramref name="original"/>
    /// (Index is -1 when the spelling could not be found inside its word). Null when the typed
    /// words do not normalise to the same tokens.
    /// </summary>
    private static List<(string Typed, int Index, bool WordHasDigit)>? TypedTokens(
        string original,
        IReadOnlyList<string> normalizedTokens,
        Func<string, string> normalizeExact,
        Regex tokenPattern)
    {
        var result = new List<(string Typed, int Index, bool WordHasDigit)>(normalizedTokens.Count);
        foreach (Match word in TypedWordRegex().Matches(original ?? string.Empty))
        {
            var searchFrom = 0;
            var hasDigit = word.Value.Any(char.IsDigit);
            foreach (Match part in tokenPattern.Matches(normalizeExact(word.Value)))
            {
                if (part.Length == 0) continue;
                var at = word.Value.IndexOf(part.Value, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (at >= 0) searchFrom = at + part.Length;
                result.Add(at >= 0
                    ? (word.Value.Substring(at, part.Length), word.Index + at, hasDigit)
                    : (word.Value, -1, hasDigit));
            }
        }

        return result.Count == normalizedTokens.Count ? result : null;
    }

    /// <summary>
    /// The correction written into the typed text: each replaced token takes the place of its
    /// typed spelling, in the same case style (Hydrabad → Hyderabad, HYDRABAD → HYDERABAD).
    /// Null when a replaced token cannot be found in the typed text.
    /// </summary>
    public static string? ReplaceInTypedText(
        string original,
        IReadOnlyList<string> normalizedTokens,
        IReadOnlyDictionary<int, string> replacements,
        Func<string, string> normalizeExact,
        Regex tokenPattern)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        var tokens = TypedTokens(original, normalizedTokens, normalizeExact, tokenPattern);
        if (tokens is null || replacements.Keys.Any(index => index >= tokens.Count || tokens[index].Index < 0)) return null;

        var spans = tokens.Select(token => (Index: Math.Max(0, token.Index), Length: token.Typed.Length)).ToArray();
        var cased = replacements.ToDictionary(pair => pair.Key, pair => InTypedCase(tokens[pair.Key].Typed, pair.Value));
        return ApplyReplacements(original, spans, cased);
    }

    private static string InTypedCase(string typed, string replacement)
    {
        var letters = typed.Where(char.IsLetter).ToArray();
        if (letters.Length > 1 && letters.All(char.IsUpper)) return replacement.ToUpperInvariant();
        if (letters.Length > 0 && char.IsUpper(letters[0]) && replacement.Length > 0)
        {
            return char.ToUpperInvariant(replacement[0]) + replacement[1..];
        }

        return replacement;
    }

    /// <summary>Replaces the tokens at the given spans of <paramref name="text"/>, keeping everything between them.</summary>
    public static string ApplyReplacements(
        string text,
        IReadOnlyList<(int Index, int Length)> tokenSpans,
        IReadOnlyDictionary<int, string> replacements)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokenSpans);
        ArgumentNullException.ThrowIfNull(replacements);

        var builder = new System.Text.StringBuilder(text.Length + 16);
        var position = 0;
        for (var index = 0; index < tokenSpans.Count; index++)
        {
            if (!replacements.TryGetValue(index, out var replacement) || string.IsNullOrWhiteSpace(replacement)) continue;
            var (start, length) = tokenSpans[index];
            builder.Append(text, position, start - position).Append(replacement.Trim());
            position = start + length;
        }

        return builder.Append(text, position, text.Length - position).ToString();
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

    [GeneratedRegex("\\S+", RegexOptions.CultureInvariant)]
    private static partial Regex TypedWordRegex();
}
