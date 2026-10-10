using Junexis.Studio.Abstractions;
using System;

namespace Junexis.Studio.Host;

/// <summary>
/// Rejestruje bezpieczne komendy wejściowe VCS.
/// </summary>
internal sealed class GitExtension : IJunexisExtension
{
    /// <inheritdoc />
    public string Id => "junexis.vcs.commands";

    /// <inheritdoc />
    public void Initialize(IHostContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        context.Commands.Register(
            id: "vcs.commit",
            execute: () => context.EventBus.Publish(
                new ActivatePanelRequested("junexis.vcs.changes")),
            menuPath: "VCS/Commit...",
            displayName: "Commit...",
            category: "VCS",
            contextTarget: "junexis.vcs.changes");

        context.Commands.Register(
            id: "vcs.repository",
            execute: () => context.EventBus.Publish(
                new ActivatePanelRequested("junexis.vcs.repository")),
            menuPath: "VCS/Repository...",
            displayName: "Repository...",
            category: "VCS",
            contextTarget: "junexis.vcs.repository");

        context.Output.Write("Extensions", "GitExtension loaded");
    }

    /// <inheritdoc />
    public void Shutdown()
    {
        // no-op (v0.1)
    }
}
