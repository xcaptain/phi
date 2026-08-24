using Phi.Agent;
using Phi.Chat;
using Phi.Extensions;
using Phi.Extensions.Host;
using Phi.Extensions.Rendering;
using Phi.Tests.Helpers;
using Phi.Tui.Components.ToolCards;

namespace Phi.Tests;

/// <summary>
/// Sprint 4 integration: extension-registered tool descriptors / card
/// renderers flow through <see cref="IExtensionRenderers"/> into the
/// projector's <see cref="ToolCallLine"/> and into the TUI's
/// <see cref="ToolCardRegistry"/>. Uses <see cref="MockSession"/> so the
/// harness events can be emitted directly.
/// </summary>
[NotInParallel(TuiTestGroups.BindingManager)]
public class ChatTranscriptProjectorExtensionTests
{
    /// <summary>Fake renderer registry that overrides "deploy" descriptor + card.</summary>
    private sealed class DeployRenderers : IExtensionRenderers
    {
        public static readonly ToolDescriptor DeployDescriptor = new(ToolKind.Generic, "deploy", "🚀");

        public bool TryGetToolDescriptor(string toolName, out ToolDescriptor descriptor)
        {
            if (toolName == "deploy")
            {
                descriptor = DeployDescriptor;
                return true;
            }
            descriptor = ToolDescriptors.For(toolName);
            return false;
        }

        public bool TryGetToolCardRenderer(string toolName, out object renderer)
        {
            if (toolName == "deploy")
            {
                renderer = new ToolCardRenderer((args, result) => $"deploy → {result.Text}");
                return true;
            }
            renderer = null!;
            return false;
        }

        public bool TryGetTranscriptLineRenderer(string lineType, out object renderer)
        {
            renderer = null!;
            return false;
        }
    }

    [Test]
    public async Task Projector_Uses_ExtensionDescriptor_ForToolCallLine()
    {
        var session = new MockSession();
        using var projector = new ChatTranscriptProjector(session, new DeployRenderers());

        var call = new ToolCall("c1", "deploy")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["env"] = "prod" },
        };
        session.EmitHarnessEvent(new AssistantToolCallEvent(call));

        var toolLine = projector.Current.OfType<ToolCallLine>().Single();
        await Assert.That(toolLine.ToolName).IsEqualTo("deploy");
        await Assert.That(toolLine.Descriptor.IconKey).IsEqualTo("🚀");
    }

    [Test]
    public async Task TuiRegistry_Returns_CustomToolCard_WhenRendererRegistered()
    {
        var renderers = new DeployRenderers();
        var card = ToolCardRegistry.For("deploy", renderers);

        await Assert.That(card).IsTypeOf<CustomToolCard>();

        // Completing the card invokes the renderer and uses its Visual as
        // the body.
        var toolCall = new ToolCall("c1", "deploy")
        {
            Arguments = new System.Text.Json.Nodes.JsonObject { ["env"] = "prod" },
        };
        card.ShowPending(toolCall);
        card.Complete(new ToolResult([new TextBlock("success")]));
        await Assert.That(card.Visual).IsNotNull();
    }

    [Test]
    public async Task TuiRegistry_FallsBack_ToBuiltIn_WhenNoRenderer()
    {
        // No renderer for "bash" → the built-in BashToolCard.
        var renderers = new DeployRenderers();
        var card = ToolCardRegistry.For("bash", renderers);

        await Assert.That(card).IsTypeOf<BashToolCard>();
    }

    [Test]
    public async Task TuiCustomCard_RendererThrowing_FallsBackToGenericBody()
    {
        var renderers = new ThrowingRenderers();
        var card = ToolCardRegistry.For("boom", renderers);

        var toolCall = new ToolCall("c1", "boom") { Arguments = new System.Text.Json.Nodes.JsonObject() };
        card.ShowPending(toolCall);
        // Must not throw; body falls back to the truncated output.
        card.Complete(new ToolResult([new TextBlock("output")]));

        await Assert.That(card.Visual).IsNotNull();
    }

    /// <summary>Renderer that throws — the transcript must survive it.</summary>
    private sealed class ThrowingRenderers : IExtensionRenderers
    {
        public bool TryGetToolDescriptor(string toolName, out ToolDescriptor descriptor)
        {
            descriptor = ToolDescriptors.For(toolName);
            return false;
        }

        public bool TryGetToolCardRenderer(string toolName, out object renderer)
        {
            if (toolName == "boom")
            {
                renderer = new ToolCardRenderer((_, _) => throw new InvalidOperationException("boom"));
                return true;
            }
            renderer = null!;
            return false;
        }

        public bool TryGetTranscriptLineRenderer(string lineType, out object renderer)
        {
            renderer = null!;
            return false;
        }
    }
}
