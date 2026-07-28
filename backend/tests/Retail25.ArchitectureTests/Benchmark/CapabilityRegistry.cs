using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Retail25.ArchitectureTests.Benchmark;

/// <summary>How a capability's presence is proven.</summary>
public enum RequirementKind
{
    /// <summary>A type must exist, by full name.</summary>
    Type = 0,

    /// <summary>A member must exist on a type, written as <c>Full.Type.Name#Member</c>.</summary>
    Member = 1,

    /// <summary>A test method must exist, proving the behaviour is not merely present but exercised.</summary>
    Test = 2,
}

/// <summary>One piece of evidence that a capability is really implemented.</summary>
/// <param name="Kind">What sort of evidence.</param>
/// <param name="Value">Fully-qualified name to resolve.</param>
public sealed record CapabilityRequirement(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] RequirementKind Kind,
    string Value);

/// <summary>
/// One legacy behaviour from the parity matrix, together with the code that must exist for it to
/// be considered delivered.
/// </summary>
/// <param name="Id">Stable identifier, e.g. <c>POS-012</c>.</param>
/// <param name="Area">Grouping used in the report.</param>
/// <param name="Feature">What the legacy system does.</param>
/// <param name="GuideRef">Page in the Retail Plus 2.5 user guide.</param>
/// <param name="Phase">Delivery phase from doc 11.</param>
/// <param name="Requires">Evidence. An empty list means nothing has been claimed yet.</param>
public sealed record Capability(
    string Id,
    string Area,
    string Feature,
    string GuideRef,
    int Phase,
    IReadOnlyList<CapabilityRequirement> Requires);

/// <summary>How much of a capability could be proven.</summary>
public enum CapabilityState
{
    /// <summary>No evidence exists, or none was claimed.</summary>
    Missing = 0,

    /// <summary>Some evidence resolved, some did not — usually a model without behaviour.</summary>
    Partial = 1,

    /// <summary>Every piece of claimed evidence resolved.</summary>
    Implemented = 2,
}

/// <summary>The outcome of checking one capability against the compiled assemblies.</summary>
/// <param name="Capability">What was checked.</param>
/// <param name="State">The verdict.</param>
/// <param name="Resolved">Evidence that was found.</param>
/// <param name="Unresolved">Evidence that was claimed but does not exist.</param>
public sealed record CapabilityResult(
    Capability Capability,
    CapabilityState State,
    IReadOnlyList<string> Resolved,
    IReadOnlyList<string> Unresolved);

/// <summary>
/// Loads the capability registry and checks each entry against the code that is actually compiled.
/// <para>
/// The point is that the benchmark cannot be talked into a better score. A capability counts as
/// delivered only when the types and members it names can be resolved by reflection, so a row
/// cannot be ticked by editing a document.
/// </para>
/// </summary>
public static class CapabilityRegistry
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<Capability> Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<Capability>>(json, SerializerOptions)
               ?? throw new InvalidOperationException($"The capability registry at {path} is empty or malformed.");
    }

    public static IReadOnlyList<CapabilityResult> Evaluate(
        IReadOnlyList<Capability> capabilities,
        IReadOnlyList<Assembly> assemblies)
    {
        var results = new List<CapabilityResult>(capabilities.Count);

        foreach (var capability in capabilities)
        {
            var resolved = new List<string>();
            var unresolved = new List<string>();

            foreach (var requirement in capability.Requires)
            {
                if (Exists(requirement, assemblies))
                {
                    resolved.Add(requirement.Value);
                }
                else
                {
                    unresolved.Add(requirement.Value);
                }
            }

            var state = capability.Requires.Count == 0 || resolved.Count == 0
                ? CapabilityState.Missing
                : unresolved.Count == 0
                    ? CapabilityState.Implemented
                    : CapabilityState.Partial;

            results.Add(new CapabilityResult(capability, state, resolved, unresolved));
        }

        return results;
    }

    private static bool Exists(CapabilityRequirement requirement, IReadOnlyList<Assembly> assemblies)
        => requirement.Kind switch
        {
            RequirementKind.Type => FindType(requirement.Value, assemblies) is not null,
            RequirementKind.Member or RequirementKind.Test => MemberExists(requirement.Value, assemblies),
            _ => false,
        };

    private static bool MemberExists(string reference, IReadOnlyList<Assembly> assemblies)
    {
        var separator = reference.LastIndexOf('#');
        if (separator <= 0)
        {
            return false;
        }

        var type = FindType(reference[..separator], assemblies);
        if (type is null)
        {
            return false;
        }

        var memberName = reference[(separator + 1)..];

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        return type.GetMember(memberName, flags).Length > 0;
    }

    private static System.Type? FindType(string fullName, IReadOnlyList<Assembly> assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var type = assembly.GetType(fullName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }

            // Nested types are written with a dot in the registry for readability, so fall back to
            // a name match rather than forcing '+' syntax on whoever maintains the file.
            var byName = assembly.GetTypes().FirstOrDefault(t => t.FullName?.Replace('+', '.') == fullName);
            if (byName is not null)
            {
                return byName;
            }
        }

        return null;
    }
}
