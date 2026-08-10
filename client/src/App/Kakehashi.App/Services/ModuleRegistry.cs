using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Kakehashi.SharedKernel;
using Kakehashi.UI.Contracts;
using Kakehashi.UI.Contracts.Services.Platform;

namespace Kakehashi.App.Services {
  /// <summary>
  /// Default <see cref="IModuleRegistry"/>: the composed modules plus a locally persisted set of
  /// detached names. Default-attached semantics — a module absent from the persisted set is
  /// attached, so a newly compiled-in module appears automatically. Attach/detach are UI-thread
  /// operations (like the rest of the settings store); each change broadcasts a
  /// <see cref="ModuleSetChangedMessage"/>.
  /// </summary>
  public sealed class ModuleRegistry : IModuleRegistry {
    private const string _detachedKey = "Modules.Detached";

    public static readonly Error UnknownModule = new(
        "Modules.Unknown", "No module with that name is composed into the application.");
    public static readonly Error RequiredModule = new(
        "Modules.Required", "Required modules cannot be detached.");
    public static readonly Error WithheldModule = new(
        "Modules.Withheld", "Your account is not assigned this module. Ask an administrator.");
    public static readonly Error GrantedModule = new(
        "Modules.Granted", "An administrator assigned this module; it cannot be detached.");

    private readonly ILocalSettingsService _localSettings;
    private readonly IReadOnlyList<IModule> _all;
    private readonly HashSet<string> _detached;

    // What the server says about this account, by SERVER module id — which is not IModule.Name.
    // Both start empty and stay empty until the fetch after sign-in returns, which is what makes
    // a failed or slow fetch leave the app exactly as a build without assignments would behave.
    // Failing open here is safe precisely because it is not the enforcement: the server refuses an
    // unassigned module's requests whatever this object believes.
    private HashSet<string> _withheld = new(StringComparer.Ordinal);
    private HashSet<string> _granted = new(StringComparer.Ordinal);

    public ModuleRegistry(IEnumerable<IModule> modules, ILocalSettingsService localSettings) {
      ArgumentNullException.ThrowIfNull(modules);
      ArgumentNullException.ThrowIfNull(localSettings);
      _localSettings = localSettings;
      _all = [.. modules];
      _detached = new HashSet<string>(
          _localSettings.Read<List<string>>(_detachedKey) ?? [], StringComparer.Ordinal);
    }

    public IReadOnlyList<IModule> All => _all;

    public IReadOnlyList<IModule> Attached => [.. _all.Where(IsAttachedCore)];

    public bool IsAttached(string moduleName) {
      return Find(moduleName) is { } module && IsAttachedCore(module);
    }

    public bool IsWithheld(string moduleName) {
      return Find(moduleName) is { } module && IsWithheldCore(module);
    }

    public bool IsGranted(string moduleName) {
      return Find(moduleName) is { } module
          && module.Descriptor.AssignmentId is { } id
          && _granted.Contains(id);
    }

    public void SetAssignments(
        IReadOnlyCollection<string> withheld, IReadOnlyCollection<string> granted) {
      ArgumentNullException.ThrowIfNull(withheld);
      ArgumentNullException.ThrowIfNull(granted);

      _withheld = new HashSet<string>(withheld, StringComparer.Ordinal);
      _granted = new HashSet<string>(granted, StringComparer.Ordinal);

      // Broadcast without saving: this is the server's answer, not the user's preference, and
      // persisting it would mean a stale copy outliving the account it described.
      WeakReferenceMessenger.Default.Send(new ModuleSetChangedMessage());
    }

    public Result Attach(string moduleName) {
      if (Find(moduleName) is not { } module) {
        return Result.Failure(UnknownModule);
      }

      if (IsWithheldCore(module)) {
        // Refused here so the user gets a sentence instead of a page that loads and then fails on
        // its first request. The server would refuse it anyway.
        return Result.Failure(WithheldModule);
      }

      if (_detached.Remove(module.Name)) {
        SaveAndBroadcast();
      }

      return Result.Success();
    }

    public Result Detach(string moduleName) {
      if (Find(moduleName) is not { } module) {
        return Result.Failure(UnknownModule);
      }

      if (module.Descriptor.IsRequired) {
        return Result.Failure(RequiredModule);
      }

      if (IsGranted(module.Name)) {
        // An assignment is a ceiling the user may sit under, not a floor they may leave: a module
        // an administrator deliberately granted is one the account is expected to have.
        return Result.Failure(GrantedModule);
      }

      if (_detached.Add(module.Name)) {
        SaveAndBroadcast();
      }

      return Result.Success();
    }

    private IModule? Find(string moduleName) {
      return _all.FirstOrDefault(
          module => string.Equals(module.Name, moduleName, StringComparison.Ordinal));
    }

    private bool IsAttachedCore(IModule module) {
      // Withheld wins over everything, including Required: a module the server refuses is not one
      // this client can present, whatever the module says about itself.
      if (IsWithheldCore(module)) {
        return false;
      }

      // A required module counts as attached even if a stale settings file says otherwise.
      return module.Descriptor.IsRequired || !_detached.Contains(module.Name);
    }

    private bool IsWithheldCore(IModule module) {
      return module.Descriptor.AssignmentId is { } id && _withheld.Contains(id);
    }

    private void SaveAndBroadcast() {
      _localSettings.Save(_detachedKey, _detached.ToList());
      WeakReferenceMessenger.Default.Send(new ModuleSetChangedMessage());
    }
  }
}
