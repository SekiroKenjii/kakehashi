using System.Threading;
using System.Threading.Tasks;

namespace __ROOT_NAMESPACE__.UI.Contracts;

/// <summary>
/// A module's contribution to the Home page's getting-started checklist: one line, and the
/// module's own answer to whether the developer has done it.
/// </summary>
/// <remarks>
/// The host cannot ask a module this directly. A module the scaffold left out, or that
/// <c>kakehashi remove module</c> took back, is not there to be asked, and a host that named it
/// anyway would stop compiling the moment somebody removed it. So a module that has a step
/// registers one, and the checklist is whatever the container was given.
/// </remarks>
public interface IGettingStartedStep
{
    /// <summary>The contributing module, matching <see cref="IModule.Name"/>.</summary>
    /// <remarks>
    /// The checklist opens the module's first navigation entry when the line is clicked, which is
    /// why the step names the module rather than a page: what a module's pages are is the module's
    /// business.
    /// </remarks>
    string ModuleName { get; }

    /// <summary>The checklist line.</summary>
    string Title { get; }

    /// <summary>One line under it, saying what doing it involves.</summary>
    string Subtitle { get; }

    /// <summary>Whether the module's own state says the step is done.</summary>
    Task<bool> IsDoneAsync(CancellationToken cancellationToken);
}
