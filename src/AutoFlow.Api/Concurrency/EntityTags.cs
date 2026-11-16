using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace AutoFlow.Api.Concurrency;

/// <summary>
/// Entity tags for aggregate versions: a strong tag holding the version
/// number (<c>"815"</c>). Clients treat it as opaque.
/// </summary>
internal static class EntityTags
{
    public static string Format(uint version) => $"\"{version}\"";

    /// <summary>
    /// Whether an <c>If-Match</c> header admits <paramref name="version"/>:
    /// <c>*</c>, or one of the listed tags under strong comparison (RFC 9110,
    /// 13.1.1). Weak or malformed tags never match.
    /// </summary>
    public static bool Matches(StringValues ifMatch, uint version)
    {
        if (!EntityTagHeaderValue.TryParseList(ifMatch.OfType<string>().ToList(), out var tags))
        {
            return false;
        }

        var current = new EntityTagHeaderValue(Format(version));
        return tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: true));
    }
}
