namespace Kakehashi.Modules.Activity.Application.Activity {
  // Spelled as the server names them, and duplicated here on purpose — the module boundary, the
  // same trade the kind strings make. It buys re-labelling or re-ordering the chips without a
  // server release, and a category the server invents later still arrives and still counts, with
  // no chip until somebody adds one.
  public static class ActivityCategories {
    // Not a server value: the wire carries an empty string, which the server reads as "do not
    // narrow". The chip that offers everything still needs a value to hold.
    public const string All = "";

    public const string SignIn = "SignIn";

    public const string Security = "Security";

    public const string System = "System";
  }
}
