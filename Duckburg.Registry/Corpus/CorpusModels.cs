using System.Text.Json.Serialization;

namespace Duckburg.Registry.Corpus;

public sealed record CorpusDocument(
    [property: JsonPropertyName("corpus_version")] string CorpusVersion,
    [property: JsonPropertyName("generated_at")] string GeneratedAt,
    [property: JsonPropertyName("disclaimer")] string Disclaimer,
    [property: JsonPropertyName("principle")] string? Principle,
    [property: JsonPropertyName("works")] IReadOnlyList<Work> Works);

public sealed record Work(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("license")] string License,
    [property: JsonPropertyName("passage_count")] int PassageCount,
    [property: JsonPropertyName("passages")] IReadOnlyList<Passage> Passages);

public sealed record Passage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("seq")] int Seq,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("text")] string Text);

public sealed record SearchHit(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("work_id")] string WorkId,
    [property: JsonPropertyName("work_title")] string WorkTitle,
    [property: JsonPropertyName("score")] double Score);
