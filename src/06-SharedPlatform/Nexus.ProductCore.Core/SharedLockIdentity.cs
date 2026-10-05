using Nexus.DevelopmentControl.Safety;

namespace DevBridge.Workspace.Locking;

// SP1-M02 / W1-01.
//
// THIS TYPE IS A FACADE. It holds no derivation of its own.
//
// Before W1-01 this file contained a "faithful RE-IMPLEMENTATION" of Nexus.Developer's
// DevelopmentControlMutexIdentity — a second copy of the algorithm whose header comment
// asserted the two agreed. V3_M1D_LOCK_IDENTITY.md §5 measured that assertion and found it
// was prose, not a test: two implementations, two authors, nothing comparing them. W1-01
// requires exactly one implementation; the derivation now lives in
// DevelopmentControlStoreIdentity.cs and both hosts compile that one file.
//
// The public surface here is preserved byte-for-byte so no existing caller changes:
// ObjectNamePrefix, Identity, HashHex, ObjectName, FromStoreIdentity, FromWorkbookPath.
//
// The default scheme remains LEGACY. That is deliberate and is the whole point of LI-7:
// changing a lock identity is a migration, not an edit. A caller that silently started
// deriving the canonical name would hold a DIFFERENT kernel object from a writer still
// running the old code, and the two would write the same workbook unimpeded.
// Use DevelopmentControlStoreIdentity.InteropObjectNames when you need both.
public sealed record SharedLockIdentity
{
    /// <summary>Object-name prefix owned by Nexus.DevelopmentControl (must match byte-for-byte).</summary>
    public const string ObjectNamePrefix = DevelopmentControlStoreIdentity.ObjectNamePrefix;

    private SharedLockIdentity(LockIdentity inner) => Inner = inner;

    /// <summary>The scheme-resolved identity this facade wraps.</summary>
    public LockIdentity Inner { get; }

    /// <summary>The normalized store identity (a canonical full path for the workbook store).</summary>
    public string Identity => Inner.Normalized;

    /// <summary>64-char UPPERCASE SHA-256 hex of the UTF8-normalized identity.</summary>
    public string HashHex => Inner.HashHex;

    /// <summary>
    /// The deterministic, cross-process named-object name: <see cref="ObjectNamePrefix"/> + 64
    /// uppercase hex. Stable for the same governed store identity; no absolute path is embedded.
    /// </summary>
    public string ObjectName => Inner.ObjectName;

    /// <summary>Derives an identity from a store-identity string (Developer <c>FromStoreIdentity</c>).</summary>
    public static SharedLockIdentity FromStoreIdentity(string identity) =>
        new(DevelopmentControlStoreIdentity.FromStoreIdentity(identity));

    /// <summary>
    /// Derives an identity from a governed workbook path (Developer <c>FromWorkbookPath</c>:
    /// pre-normalizes with <c>Path.GetFullPath</c> before the shared normalizer).
    /// </summary>
    public static SharedLockIdentity FromWorkbookPath(string workbookPath) =>
        new(DevelopmentControlStoreIdentity.FromWorkbookPath(workbookPath));

    /// <summary>
    /// The CANONICAL identity for the same store (W1-01 target scheme). Not the default:
    /// see the type comment.
    /// </summary>
    public static SharedLockIdentity CanonicalFromWorkbookPath(string workbookPath, bool allowUnc = true) =>
        new(DevelopmentControlStoreIdentity.CanonicalFromWorkbookPath(workbookPath, allowUnc));

    /// <summary>Every object name a transitional acquirer must contend on (LI-7 option (a)).</summary>
    public static IReadOnlyList<string> InteropObjectNames(string workbookPath, bool allowUnc = true) =>
        DevelopmentControlStoreIdentity.InteropObjectNames(workbookPath, allowUnc);
}
