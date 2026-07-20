using System.Text.Json;

namespace Guessly.Api.Services;

public sealed record VocabEntry(string Word, int Length);

public sealed record SecretWordEntry(string Word, int Length, string Category);

/// <summary>
/// Loads pretrained word vectors at startup and computes per-round rank lists
/// (word -> similarity rank against a secret word) via cosine similarity.
/// The rank lookup for a round is only ever handed to server-side RoundState;
/// nothing in this class should be serialized directly to clients.
/// </summary>
public sealed class EmbeddingService
{
    private readonly string[] _words = [];
    private readonly float[,] _vectors = new float[0, 0];
    private readonly float[] _norms = [];
    private readonly Dictionary<string, int> _wordIndex = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<SecretWordEntry> _secretWords = [];
    private readonly int _dim;
    private readonly Random _random = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public int VocabularyCount => _words.Length;

    public EmbeddingService(IConfiguration config, ILogger<EmbeddingService> logger)
    {
        var dataPath = config["EmbeddingsDataPath"] ?? Path.Combine(AppContext.BaseDirectory, "EmbeddingsData");

        var metaPath = Path.Combine(dataPath, "meta.json");
        var wordsPath = Path.Combine(dataPath, "vocab_words.json");
        var vectorsPath = Path.Combine(dataPath, "vocab_vectors.f32");
        var secretWordsPath = Path.Combine(dataPath, "secret_words.json");

        if (!File.Exists(metaPath) || !File.Exists(wordsPath) || !File.Exists(vectorsPath))
        {
            logger.LogWarning(
                "Embeddings data not found at {DataPath}. Run the embeddings pipeline (see embeddings/README.md). " +
                "The server will start but rooms cannot begin rounds until data is present.",
                dataPath);
            _dim = 0;
            return;
        }

        var meta = JsonSerializer.Deserialize<MetaJson>(File.ReadAllText(metaPath), JsonOptions)
            ?? throw new InvalidOperationException("Invalid meta.json");
        _dim = meta.Dim;

        var vocab = JsonSerializer.Deserialize<List<VocabEntry>>(File.ReadAllText(wordsPath), JsonOptions)
            ?? throw new InvalidOperationException("Invalid vocab_words.json");

        var raw = File.ReadAllBytes(vectorsPath);
        var expectedBytes = (long)vocab.Count * meta.Dim * sizeof(float);
        if (raw.LongLength != expectedBytes)
            throw new InvalidOperationException(
                $"vocab_vectors.f32 size mismatch: expected {expectedBytes} bytes for {vocab.Count}x{meta.Dim}, got {raw.LongLength}.");

        _words = vocab.Select(v => v.Word).ToArray();
        _vectors = new float[vocab.Count, meta.Dim];
        Buffer.BlockCopy(raw, 0, _vectors, 0, raw.Length);

        _norms = new float[vocab.Count];
        for (var i = 0; i < vocab.Count; i++)
        {
            double sumSq = 0;
            for (var d = 0; d < meta.Dim; d++)
                sumSq += (double)_vectors[i, d] * _vectors[i, d];
            _norms[i] = (float)Math.Sqrt(sumSq);
        }

        for (var i = 0; i < _words.Length; i++)
            _wordIndex[_words[i]] = i;

        if (File.Exists(secretWordsPath))
        {
            _secretWords = JsonSerializer.Deserialize<List<SecretWordEntry>>(File.ReadAllText(secretWordsPath), JsonOptions)
                ?? [];
        }

        logger.LogInformation(
            "Loaded {Count} vocabulary words (dim={Dim}) and {SecretCount} secret-word candidates from {DataPath}.",
            _words.Length, _dim, _secretWords.Count, dataPath);
    }

    public bool IsReady => _dim > 0;

    public bool IsInVocabulary(string word) => _wordIndex.ContainsKey(word);

    public bool HasSecretWordCandidates(int minLength, int maxLength) =>
        _secretWords.Any(w => w.Length >= minLength && w.Length <= maxLength);

    /// <summary>Picks a random secret word within [minLength, maxLength], excluding any already used in this game.</summary>
    public string PickSecretWord(int minLength, int maxLength, IReadOnlySet<string> exclude)
    {
        var candidates = _secretWords
            .Where(w => w.Length >= minLength && w.Length <= maxLength && !exclude.Contains(w.Word))
            .ToList();

        if (candidates.Count == 0)
        {
            // Fall back to the full secret-word pool ignoring the exclude set if we've exhausted candidates.
            candidates = _secretWords.Where(w => w.Length >= minLength && w.Length <= maxLength).ToList();
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No secret-word candidates available for length range {minLength}-{maxLength}.");

        return candidates[_random.Next(candidates.Count)].Word;
    }

    /// <summary>Computes the full word -> rank map for a secret word. Rank 1 = the secret word itself.</summary>
    public IReadOnlyDictionary<string, int> ComputeRankLookup(string secretWord)
    {
        if (!_wordIndex.TryGetValue(secretWord, out var targetIdx))
            throw new InvalidOperationException($"Secret word '{secretWord}' not found in vocabulary.");

        var n = _words.Length;
        var sims = new float[n];
        var targetNorm = _norms[targetIdx];

        for (var i = 0; i < n; i++)
        {
            double dot = 0;
            for (var d = 0; d < _dim; d++)
                dot += (double)_vectors[i, d] * _vectors[targetIdx, d];
            sims[i] = (float)(dot / ((double)_norms[i] * targetNorm + 1e-8));
        }

        var order = Enumerable.Range(0, n).OrderByDescending(i => sims[i]).ToArray();

        var lookup = new Dictionary<string, int>(n, StringComparer.OrdinalIgnoreCase);
        for (var rank = 0; rank < n; rank++)
            lookup[_words[order[rank]]] = rank + 1;

        return lookup;
    }

    private sealed record MetaJson(int Dim, int Count, string Source);
}
