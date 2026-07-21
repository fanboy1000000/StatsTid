namespace StatsTid.Backend.Api.Contracts;

// S118 / TASK-11800 (Fork B retrofit Pass 5, PAT-010/PAT-012) — the named response record for
// the wage-type-mapping admin endpoints (WageTypeMappingEndpoints). An EXACT shape-copy of
// the anonymous object the handlers previously returned: same member NAMES, same ORDER, same
// nullability — camelCase via JsonSerializerDefaults.Web, NO [JsonPropertyName].
// BYTE-IDENTICAL wire JSON on all five ops (the POST 201 already built this exact shape from
// request locals — the pre-S112 precedent for fork-free creates; it now builds the record).
//
// NO [AllowedValues] here: timeType / wageType / agreementCode / okVersion / position are all
// agreement-defining OPEN sets (P4-open BY DESIGN, per the S118 exclusions — the WTM
// Case-A/B/C + supersession write machinery is untouched).

/// <summary>
/// The 7-member wage-type-mapping row — shared by the list, agreement-list, POST 201 and
/// PUT 200 sites. <c>version</c> is the row-version token; the list responses are the single
/// source of truth for If-Match composition (composite natural key — no by-id GET exists).
/// The 412 error-body <c>currentState</c> envelopes stay anonymous/untyped (S118 exclusion).
/// </summary>
public sealed record WageTypeMappingResponse(
    string TimeType,
    string WageType,
    string OkVersion,
    string AgreementCode,
    string Position,
    string? Description,
    long Version);
