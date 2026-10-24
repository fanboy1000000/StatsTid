using Npgsql;
using StatsTid.Infrastructure;
using StatsTid.SharedKernel.Events;

namespace StatsTid.Tests.Regression.Infrastructure;

/// <summary>
/// S126 / F5 — the bounded "latest event of type T" reads that replaced three full-stream replays
/// plus one hand-rolled inline query.
///
/// <para><b>The defect.</b> <c>employee-{id}</c> is the CONSOLIDATED stream (ADR-018 D6): it carries
/// every time registration, entitlement revaluation, waiver, feriehindring and termination payout an
/// employee accumulates. Three read paths (<c>/balance/{id}/summary</c>, the year overview, and
/// <c>/flex-balance</c>) called <c>ReadStreamAsync</c> and ran
/// <c>.OfType&lt;T&gt;().LastOrDefault()</c> over the result — loading and
/// JSON-deserializing the employee's entire history to read one decimal. Cost grew with employment
/// length and was invisible locally, because the demo world ships ZERO time registrations.</para>
///
/// <para><b>Why the tests below use an unregistered event type as the discriminator.</b> Command
/// count cannot separate old from new (both issue one command), and a wall-clock threshold at the
/// ~1,000-event scale a real stream reaches would be a flake generator. The one DETERMINISTIC
/// difference is the behavioural delta the extraction actually introduces: the old form deserialized
/// every preceding event, so any row it could not map threw; the new form never reads those rows.
/// That makes <see cref="DeepStream_WithUnreadableEarlierRow_OldFullReplayThrows_BoundedReadSucceeds"/>
/// a genuine RED-against-the-old-implementation test rather than a restatement of the new one.</para>
/// </summary>
[Trait("Category", "Docker")]
public sealed class LatestEventReadTests : IAsyncLifetime
{
    private Segmentation.TestFixtures.DockerHarness _harness = null!;
    private DbConnectionFactory _factory = null!;

    private const string DeepStream = "employee-f5deep";
    private const string EmptyStream = "employee-f5empty";

    // Payloads must satisfy every `required` member of the target event, otherwise System.Text.Json
    // throws on deserialization and the fixture would "prove" the old path throws for the WRONG
    // reason — the failure would look identical to the unregistered-type case this test is about.
    private const string TimeEntryJson =
        """{"employeeId":"f5emp","date":"2026-01-15","hours":7.4,"agreementCode":"HK","okVersion":"2024"}""";

    // Formatted with InvariantCulture DELIBERATELY. This machine runs a Danish locale, where
    // `5.0m.ToString()` is "5,0" — interpolating a decimal straight into JSON produced
    // `"hours":5,0` and Postgres rejected it with 22P02. Worth keeping visible: it is the
    // same hazard, in miniature, that makes the production reader deserialize through EventSerializer
    // rather than pulling `data->>'hours'` and running its own decimal.TryParse.
    // S146 R-7: the sample event type was FlexBalanceUpdated (deleted with the dead /flex-balance
    // route). The reader is generic, so the meaning is unchanged with AbsenceRegistered as the
    // sample: registered, with required members of its own, and not the filler type
    // (TimeEntryRegistered). `hours` plays the part `newBalance` played.
    private static string SampleJson(decimal hours, string absenceType = "VACATION")
        => FormattableString.Invariant(
            $$"""{"employeeId":"f5emp","date":"2026-01-15","absenceType":"{{absenceType}}","hours":{{hours}},"agreementCode":"HK","okVersion":"2024"}""");

    public async Task InitializeAsync()
    {
        _harness = await Segmentation.TestFixtures.DockerHarness.StartAsync();
        await Hosting.StatsTidWebApplicationFactory.ApplyFullSchemaAsync(_harness.ConnectionString);
        _factory = new DbConnectionFactory(_harness.ConnectionString);

        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await ExecAsync(conn,
            "INSERT INTO event_streams (stream_id) VALUES (@a), (@b) ON CONFLICT DO NOTHING",
            ("a", DeepStream), ("b", EmptyStream));
    }

    public async Task DisposeAsync()
    {
        if (_harness is not null) await _harness.DisposeAsync();
    }

    /// <summary>
    /// The load-bearing one. A deep stream whose AbsenceRegistered sample sits EARLY (version 2) and whose
    /// later rows include one the serializer cannot map. The old full-replay form throws on that row
    /// before ever reaching the sample event; the bounded read never touches it and returns the right
    /// answer — so this fails against the pre-S126 implementation and passes after.
    /// </summary>
    [Fact]
    public async Task DeepStream_WithUnreadableEarlierRow_OldFullReplayThrows_BoundedReadSucceeds()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await ClearStreamAsync(conn, DeepStream);

        // v1: an ordinary event. v2: the ONE sample event, deliberately early — the worst case for a
        // backward scan, and the realistic shape for someone whose flex was adjusted once, long ago.
        await InsertEventAsync(conn, DeepStream, 1, "TimeEntryRegistered", TimeEntryJson);
        await InsertEventAsync(conn, DeepStream, 2, "AbsenceRegistered", SampleJson(12.5m, "SICK"));
        // v3: a row no registered mapper claims. The old form deserialized EVERY row, so this threw.
        await InsertEventAsync(conn, DeepStream, 3, "AnUnregisteredEventTypeFromTheFuture", """{"x": 1}""");
        for (var v = 4; v <= 60; v++)
            await InsertEventAsync(conn, DeepStream, v, "TimeEntryRegistered", TimeEntryJson);

        var store = new PostgresEventStore(_factory);

        // The OLD path, still present and still used elsewhere — proves the fixture really is
        // hostile to a full replay rather than merely asserted to be.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ReadStreamAsync(DeepStream));

        // The NEW path answers correctly over the same rows.
        var latest = await store.ReadLatestOfTypeAsync<AbsenceRegistered>(DeepStream);
        Assert.NotNull(latest);
        Assert.Equal(12.5m, latest!.Hours);
        // Every field must survive the round-trip — the reason this reads `data` through
        // EventSerializer instead of extracting one JSON key per field.
        Assert.Equal("SICK", latest.AbsenceType);
        Assert.Equal(new DateOnly(2026, 1, 15), latest.Date);
    }

    /// <summary>Highest stream_version wins, not insertion order or occurred_at.</summary>
    [Fact]
    public async Task MultipleFlexEvents_ReturnsTheHighestStreamVersion()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await ClearStreamAsync(conn, DeepStream);

        await InsertEventAsync(conn, DeepStream, 1, "AbsenceRegistered", SampleJson(1.0m));
        await InsertEventAsync(conn, DeepStream, 2, "AbsenceRegistered", SampleJson(2.0m));
        await InsertEventAsync(conn, DeepStream, 3, "AbsenceRegistered", SampleJson(3.0m));

        var store = new PostgresEventStore(_factory);
        var latest = await store.ReadLatestOfTypeAsync<AbsenceRegistered>(DeepStream);

        Assert.Equal(3.0m, latest!.Hours);
        // Equivalence with the form it replaced, asserted rather than assumed.
        var viaReplay = (await store.ReadStreamAsync(DeepStream))
            .OfType<AbsenceRegistered>().LastOrDefault();
        Assert.Equal(viaReplay!.Hours, latest.Hours);
    }

    /// <summary>No such event ⇒ null, which every caller maps to the 0m / no-history branch.</summary>
    [Fact]
    public async Task StreamWithNoEventOfThatType_ReturnsNull()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await ClearStreamAsync(conn, EmptyStream);
        await InsertEventAsync(conn, EmptyStream, 1, "TimeEntryRegistered", TimeEntryJson);

        var store = new PostgresEventStore(_factory);
        Assert.Null(await store.ReadLatestOfTypeAsync<AbsenceRegistered>(EmptyStream));
    }

    /// <summary>
    /// The batch shape used by the team read: one round-trip, latest-per-stream, and streams with no
    /// such event simply absent (the dictionary analogue of the single-stream null).
    /// </summary>
    [Fact]
    public async Task BatchShape_ReturnsLatestPerStream_AndOmitsStreamsWithNone()
    {
        await using var conn = new NpgsqlConnection(_harness.ConnectionString);
        await conn.OpenAsync();
        await ClearStreamAsync(conn, DeepStream);
        await ClearStreamAsync(conn, EmptyStream);

        await InsertEventAsync(conn, DeepStream, 1, "AbsenceRegistered", SampleJson(4.0m));
        await InsertEventAsync(conn, DeepStream, 2, "AbsenceRegistered", SampleJson(9.0m));
        await InsertEventAsync(conn, EmptyStream, 1, "TimeEntryRegistered", TimeEntryJson);

        var map = await PostgresEventStore.ReadLatestOfTypePerStreamAsync<AbsenceRegistered>(
            conn, tx: null, new[] { DeepStream, EmptyStream });

        Assert.Equal(9.0m, map[DeepStream].Hours);
        Assert.DoesNotContain(EmptyStream, map.Keys);

        // An empty input must not issue a query that matches everything.
        var none = await PostgresEventStore.ReadLatestOfTypePerStreamAsync<AbsenceRegistered>(
            conn, tx: null, Array.Empty<string>());
        Assert.Empty(none);
    }

    // ── helpers ──

    private static async Task InsertEventAsync(
        NpgsqlConnection conn, string streamId, int version, string eventType, string json)
    {
        await using var cmd = new NpgsqlCommand(
            """
            INSERT INTO events (event_id, stream_id, stream_version, event_type, data, occurred_at)
            VALUES (gen_random_uuid(), @s, @v, @t, @d::jsonb, NOW())
            """, conn);
        cmd.Parameters.AddWithValue("s", streamId);
        cmd.Parameters.AddWithValue("v", version);
        cmd.Parameters.AddWithValue("t", eventType);
        cmd.Parameters.AddWithValue("d", json);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ClearStreamAsync(NpgsqlConnection conn, string streamId)
        => await ExecAsync(conn, "DELETE FROM events WHERE stream_id = @s", ("s", streamId));

    private static async Task ExecAsync(
        NpgsqlConnection conn, string sql, params (string Name, object Value)[] ps)
    {
#pragma warning disable CA2100 // literal SQL from this file only
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        foreach (var (name, value) in ps) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }
}
