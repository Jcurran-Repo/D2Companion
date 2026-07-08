using System.Text.Json;
using D2Companion.Brain;
using Xunit;

namespace D2Companion.Tests;

public sealed class D2ToolsTests
{
    [Fact]
    public void ToolDefinitions_AreBuilt()
    {
        Assert.NotEmpty(D2Tools.Definitions);
        Assert.Contains(D2Tools.Definitions, t => t.Name == "assign_skill_point");
        Assert.Contains(D2Tools.Definitions, t => t.Name == "get_character_state");
    }

    [Fact]
    public void EveryToolSchemaProperty_IsSerializable()
    {
        // Regression guard for the static-init order bug: a default(JsonElement) is
        // ValueKind.Undefined and throws "Operation is not valid due to the current state
        // of the object" when serialized (which is what the SDK does with the tool schema).
        foreach (var tool in D2Tools.Definitions)
        {
            var properties = tool.InputSchema?.Properties;
            Assert.NotNull(properties);
            foreach (var property in properties!)
            {
                Assert.NotEqual(JsonValueKind.Undefined, property.Value.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(property.Value.GetRawText()));
            }
        }
    }

    [Fact]
    public void MutationTools_ExposeRationale()
    {
        // Every tool except the read-only ones should carry the (previously broken) rationale.
        foreach (var tool in D2Tools.Definitions)
        {
            if (tool.Name is "get_character_state" or "view_screenshot") continue;
            var properties = tool.InputSchema?.Properties;
            Assert.NotNull(properties);
            Assert.True(properties!.ContainsKey("rationale"),
                $"{tool.Name} is missing the rationale property.");
        }
    }
}
