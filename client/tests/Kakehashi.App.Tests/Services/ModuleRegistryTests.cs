using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.App.Services;
using Kakehashi.UI.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Kakehashi.App.Tests.Services {
  /// <summary>
  /// Unit tests for <see cref="ModuleRegistry"/>: attach/detach, required-module protection,
  /// default-attached semantics, persistence of the detached set, and the change broadcast.
  /// Uses a hand-rolled in-memory <see cref="ILocalSettingsService"/> so persistence round-trips
  /// are observable, and fake modules so no WinUI types are constructed.
  /// </summary>
  public sealed class ModuleRegistryTests {
    private const string _detachedKey = "Modules.Detached";

    private readonly InMemoryLocalSettings _settings = new();
    private readonly FakeModule _catalog = new("Notes", isRequired: false, assignmentId: "notes");
    private readonly FakeModule _auth = new("Auth", isRequired: true, assignmentId: "account");

    private ModuleRegistry CreateRegistry() {
      return new ModuleRegistry([_catalog, _auth], _settings);
    }

    [Fact]
    public void All_ReturnsEveryComposedModule() {
      var registry = CreateRegistry();

      Assert.Equal(["Notes", "Auth"], registry.All.Select(module => module.Name));
    }

    [Fact]
    public void Attached_ByDefault_IncludesEveryModule() {
      var registry = CreateRegistry();

      Assert.True(registry.IsAttached("Notes"));
      Assert.True(registry.IsAttached("Auth"));
      Assert.Equal(2, registry.Attached.Count);
    }

    [Fact]
    public void Detach_OptionalModule_RemovesItFromAttached() {
      var registry = CreateRegistry();

      var result = registry.Detach("Notes");

      Assert.True(result.IsSuccess);
      Assert.False(registry.IsAttached("Notes"));
      Assert.DoesNotContain(registry.Attached, module => module.Name == "Notes");
    }

    [Fact]
    public void Attach_AfterDetach_RestoresIt() {
      var registry = CreateRegistry();
      registry.Detach("Notes");

      var result = registry.Attach("Notes");

      Assert.True(result.IsSuccess);
      Assert.True(registry.IsAttached("Notes"));
    }

    [Fact]
    public void Detach_RequiredModule_FailsAndKeepsItAttached() {
      var registry = CreateRegistry();

      var result = registry.Detach("Auth");

      Assert.True(result.IsFailure);
      Assert.Equal(ModuleRegistry.RequiredModule, result.Error);
      Assert.True(registry.IsAttached("Auth"));
    }

    [Fact]
    public void Detach_UnknownModule_FailsWithUnknownError() {
      var registry = CreateRegistry();

      var result = registry.Detach("Nope");

      Assert.True(result.IsFailure);
      Assert.Equal(ModuleRegistry.UnknownModule, result.Error);
    }

    [Fact]
    public void Attach_UnknownModule_FailsWithUnknownError() {
      var registry = CreateRegistry();

      var result = registry.Attach("Nope");

      Assert.True(result.IsFailure);
      Assert.Equal(ModuleRegistry.UnknownModule, result.Error);
    }

    [Fact]
    public void IsAttached_UnknownModule_IsFalse() {
      var registry = CreateRegistry();

      Assert.False(registry.IsAttached("Nope"));
    }

    [Fact]
    public void Detach_PersistsTheDetachedSet() {
      CreateRegistry().Detach("Notes");

      var persisted = _settings.Read<List<string>>(_detachedKey);

      Assert.NotNull(persisted);
      Assert.Equal(["Notes"], persisted);
    }

    [Fact]
    public void DetachedSet_IsReadBackByANewRegistry() {
      CreateRegistry().Detach("Notes");

      // A fresh registry over the same store reflects the persisted detachment.
      var reloaded = CreateRegistry();

      Assert.False(reloaded.IsAttached("Notes"));
    }

    [Fact]
    public void RequiredModule_StaysAttached_EvenIfStoreListsIt() {
      // A stale/tampered settings file marks the required module detached; the registry ignores it.
      _settings.Save(_detachedKey, new List<string> { "Auth" });

      var registry = CreateRegistry();

      Assert.True(registry.IsAttached("Auth"));
    }

    [Fact]
    public void Detach_BroadcastsModuleSetChanged() {
      var registry = CreateRegistry();
      int received = 0;
      var recipient = new object();
      WeakReferenceMessenger.Default.Register<object, ModuleSetChangedMessage>(
          recipient, (_, _) => received++);
      try {
        registry.Detach("Notes");
      } finally {
        WeakReferenceMessenger.Default.UnregisterAll(recipient);
      }

      Assert.Equal(1, received);
    }

    [Fact]
    public void Detach_AlreadyDetachedModule_DoesNotBroadcastAgain() {
      var registry = CreateRegistry();
      registry.Detach("Notes");
      int received = 0;
      var recipient = new object();
      WeakReferenceMessenger.Default.Register<object, ModuleSetChangedMessage>(
          recipient, (_, _) => received++);
      try {
        var result = registry.Detach("Notes");
        Assert.True(result.IsSuccess);
      } finally {
        WeakReferenceMessenger.Default.UnregisterAll(recipient);
      }

      Assert.Equal(0, received);
    }

    /* --- Assignments: what the server says, as opposed to what the user prefers --- */

    [Fact]
    public void BeforeAnyFetch_EverythingBehavesAsThoughAssignmentsDidNotExist() {
      // The startup fetch may be slow, or fail. Either way the app must present exactly what a
      // build without assignments would — the server is what refuses, not this.
      var registry = CreateRegistry();

      Assert.True(registry.IsAttached("Notes"));
      Assert.False(registry.IsWithheld("Notes"));
      Assert.False(registry.IsGranted("Notes"));
    }

    [Fact]
    public void SetAssignments_KeysOnTheServerId_NotTheModuleName() {
      // Auth is called "account" on the server. A registry that matched on IModule.Name would
      // silently never apply this, which is the failure mode the AssignmentId field exists for.
      var registry = CreateRegistry();

      registry.SetAssignments(withheld: ["account"], granted: []);

      Assert.True(registry.IsWithheld("Auth"));
      Assert.False(registry.IsWithheld("Notes"));
    }

    [Fact]
    public void AWithheldModuleIsNeverAttached_EvenWhenItIsRequired() {
      var registry = CreateRegistry();

      registry.SetAssignments(withheld: ["account"], granted: []);

      // Required is a statement the module makes about itself; withheld is one the server makes
      // about the account. The server wins.
      Assert.False(registry.IsAttached("Auth"));
      Assert.DoesNotContain(registry.Attached, module => module.Name == "Auth");
    }

    [Fact]
    public void Attach_AWithheldModule_FailsWithSomethingWorthShowing() {
      var registry = CreateRegistry();
      registry.SetAssignments(withheld: ["notes"], granted: []);

      var result = registry.Attach("Notes");

      Assert.True(result.IsFailure);
      Assert.Equal(ModuleRegistry.WithheldModule, result.Error);
      Assert.False(registry.IsAttached("Notes"));
    }

    [Fact]
    public void Detach_AGrantedModule_IsRefused() {
      // Granted means an administrator assigned the module; detaching it is not the user's call.
      var registry = CreateRegistry();
      registry.SetAssignments(withheld: [], granted: ["notes"]);

      var result = registry.Detach("Notes");

      Assert.True(result.IsFailure);
      Assert.Equal(ModuleRegistry.GrantedModule, result.Error);
      Assert.True(registry.IsAttached("Notes"));
    }

    [Fact]
    public void AnUngovernedModuleStaysEntirelyTheUsersOwnChoice() {
      // "Open" is not "granted": nobody restricted this module, so detaching it is a preference
      // and must keep working.
      var registry = CreateRegistry();
      registry.SetAssignments(withheld: [], granted: ["account"]);

      Assert.False(registry.IsGranted("Notes"));
      Assert.True(registry.Detach("Notes").IsSuccess);
      Assert.False(registry.IsAttached("Notes"));
    }

    [Fact]
    public void SetAssignments_ReplacesRatherThanMerges() {
      // Signing in as somebody else on a shared machine must not inherit their predecessor's
      // access.
      var registry = CreateRegistry();
      registry.SetAssignments(withheld: ["notes"], granted: []);
      Assert.True(registry.IsWithheld("Notes"));

      registry.SetAssignments(withheld: [], granted: []);

      Assert.False(registry.IsWithheld("Notes"));
      Assert.True(registry.IsAttached("Notes"));
    }

    [Fact]
    public void SetAssignments_DoesNotPersist() {
      // The server's answer is not the user's preference. Persisting it would leave a stale copy
      // outliving the account it described.
      var registry = CreateRegistry();

      registry.SetAssignments(withheld: ["notes"], granted: []);

      Assert.Null(_settings.Read<List<string>>(_detachedKey));
    }

    /// <summary>A minimal IModule that constructs no WinUI types.</summary>
    private sealed class FakeModule : IModule {
      public FakeModule(string name, bool isRequired, string? assignmentId = null) {
        Name = name;
        Descriptor = new ModuleDescriptor(
            name, $"{name} description", isRequired, assignmentId);
      }

      public string Name { get; }

      public ModuleDescriptor Descriptor { get; }

      public void RegisterServices(IServiceCollection services) { }

      public IReadOnlyList<NavigationItem> GetNavigationItems() {
        return [];
      }
    }
  }
}
