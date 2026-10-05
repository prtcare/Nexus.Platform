// DevelopmentControlStoreIdentity.cs — W1-01 CANONICAL STORE / LOCK IDENTITY.
//
// THE SINGLE CANONICAL IMPLEMENTATION. This file is the one authoritative source of the
// DevelopmentControl store/lock identity derivation, and the only place the algorithm is
// written. Nexus.Developer's DevelopmentControlMutexIdentity.cs is a COMPATIBILITY FACADE
// over it — not a second implementation.
//
// TWO COPIES, ONE ALGORITHM (W1 Closure Task 4)
// ---------------------------------------------
// Before W1 Closure this file reached Nexus.Developer.Core through a <Compile Include> that
// named the Forge worktree by relative path. That coupling is gone: the file now exists in
// BOTH repositories as a byte-identical copy and each host compiles its own.
//
// Two physical copies are the residual LI-1 tension, and it is stated rather than hidden.
// W1-11 forbids the Platform/Layer-06 extraction that would give the algorithm one neutral
// home, and no package feed reachable from this device can carry a shared assembly. The two
// copies are therefore held together by MEASUREMENT, not by convention:
//
//   * PROVENANCE.md, beside this file in each repository, pins its SHA-256. Each host
//     asserts that pin in its own suite, so a copy edited on either side fails THAT side's
//     tests. No host reads the other's worktree to do it.
//   * Both hosts assert the same 19 frozen M1D vectors against the same expected hashes,
//     and both can dump the table they computed, so the two derivations are compared
//     byte-for-byte rather than assumed equal.
//
// The correct end state remains one referenced assembly — W1_CHANGE_MANIFEST.md D-2. This
// is the transitional form that removes the cross-worktree dependency without one.
//
// WHY THIS FILE EXISTS
// --------------------
// V3_M1D_LOCK_IDENTITY.md measured two independent implementations of the same algorithm
// (Forge: SharedLockIdentity.cs; Developer: DevelopmentControlMutexIdentity.cs). They agree
// today, by accident, with nothing asserting it and nothing comparing them. The larger
// finding was stronger still: Forge's *production* write path does not use the identity at
// all — its live lock is a PID text file (WorkbookWriterGate).
//
// W1-01 therefore requires exactly one implementation, consumed by both hosts, with
// executable cross-host tests. This is that implementation.
//
// TWO SCHEMES, DELIBERATELY
// -------------------------
// LI-7 freezes that changing a lock identity is a MIGRATION, not an edit. A pre-change and a
// post-change writer holding *different* object names over the same workbook is exactly the
// silent dual-writer defect the lock exists to prevent. So this type implements both:
//
//   Legacy    — byte-identical to the algorithm shipping today, in both hosts. Every one of
//               the 19 frozen M1D vectors reproduces exactly under this scheme. Group C
//               divergence canaries (V09, V10, V14-V17) are PRESERVED, not "fixed".
//
//   Canonical — the corrected derivation (M1D corrections F-1..F-5). It is a REFINEMENT,
//               not a replacement: for every input on which Legacy is well-defined and
//               platform-independent, Canonical produces the same normalised string. It
//               differs only where Legacy is CWD-dependent (§F-2), platform-dependent
//               (§F-5), token-ambiguous (§F-3) or destroys a root (§F-4).
//
// A transitional acquirer contending across a staged migration must take BOTH names
// (see InteropObjectNames). That is the mechanism LI-7 option (a) requires, and it is what
// makes the migration safe rather than silently lossy.
//
// ZERO-IO BY DEFAULT
// ------------------
// The derivation itself touches no file system and reads no clock, so Forge can derive an
// identity with Nexus down (V3_M1C_CONTROL_AUTHORITY.md §3, the BOOTSTRAP_SAFE tier).
// The one operation that genuinely needs the file system — resolving a UNC share or an 8.3
// short name to the canonical path (M1D F-1) — is exposed separately as TryResolveFinalPath
// and is never called implicitly.
// The nullable annotations below are load-bearing (a null Raw is the W1-07 blank case), so the
// context is declared in the file rather than inherited from the project: the file is compiled
// into both DevBridge.Workspace.Locking and Nexus.Developer.Core, whose project-level settings
// differ.
#nullable enable

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Nexus.DevelopmentControl.Safety;

/// <summary>Which derivation produced an identity. Recorded so a caller can never mistake a
/// transitional legacy name for the canonical one.</summary>
public enum LockIdentityScheme
{
    /// <summary>Byte-identical to the algorithm shipping in Forge and Developer today.</summary>
    Legacy = 0,

    /// <summary>The corrected derivation (M1D F-1..F-5). Target of the staged migration.</summary>
    Canonical = 1,
}

/// <summary>
/// The derived identity of a governed DevelopmentControl store.
/// <para><see cref="Normalized"/> is the canonicalised input that was actually hashed.
/// <see cref="HashHex"/> is the 64-char UPPERCASE SHA-256 hex of its UTF-8 bytes.
/// <see cref="ObjectName"/> is <see cref="ObjectNamePrefix"/> + <see cref="HashHex"/> and is
/// the cross-process named-object name. <see cref="LockFileName"/> is the deterministic file
/// lock name.</para>
/// </summary>
public sealed record LockIdentity
{
    /// <summary>Object-name prefix owned by Nexus.DevelopmentControl (must match byte-for-byte).</summary>
    public const string ObjectNamePrefix = "NexusDevelopmentControl_";

    internal LockIdentity(string normalized, string hashHex, LockIdentityScheme scheme)
    {
        Normalized = normalized;
        HashHex = hashHex;
        Scheme = scheme;
        ObjectName = ObjectNamePrefix + hashHex;
        LockFileName = hashHex + ".lock";
    }

    /// <summary>The normalised store identity — a canonical full path for a workbook store.</summary>
    public string Normalized { get; }

    /// <summary>64-char UPPERCASE SHA-256 hex of the UTF-8 normalised identity.</summary>
    public string HashHex { get; }

    /// <summary>The derived cross-process named-object name.</summary>
    public string ObjectName { get; }

    /// <summary>The deterministic file-lock file name (no directory).</summary>
    public string LockFileName { get; }

    /// <summary>Which scheme produced this identity.</summary>
    public LockIdentityScheme Scheme { get; }
}

/// <summary>Raised when an input cannot be given a stable identity. A derivation that cannot
/// fail is the false-safety pattern; this type refuses ambiguous input instead of guessing.</summary>
public sealed class StoreIdentityException : Exception
{
    public StoreIdentityException(string message) : base(message) { }
}

public static class DevelopmentControlStoreIdentity
{
    // ---------------------------------------------------------------- declared algorithm
    // Exactly one hash algorithm is declared for the whole system. Changing it is an identity
    // migration (LI-7), never a local edit.
    public const string HashAlgorithmName = "SHA-256";
    public const int HashHexLength = 64;
    public const string ObjectNamePrefix = LockIdentity.ObjectNamePrefix;

    // ================================================================ public entry points

    /// <summary>
    /// Derives the LEGACY identity from a store-identity string. Backward compatible: this is
    /// the exact behaviour shipping today, including its CWD-dependence for relative inputs
    /// and its platform-conditional case folding. Retained so existing lock holders keep
    /// contending during the migration.
    /// </summary>
    public static LockIdentity FromStoreIdentity(string identity) => Legacy(identity);

    /// <summary>Derives the LEGACY identity from a governed workbook path.</summary>
    public static LockIdentity FromWorkbookPath(string workbookPath)
    {
        ArgumentNullException.ThrowIfNull(workbookPath);
        return Legacy(Path.GetFullPath(workbookPath));
    }

    /// <summary>Derives the LEGACY identity from a raw (already-formed) input.</summary>
    public static LockIdentity Legacy(string identity) =>
        Build(NormalizeLegacy(Require(identity)), LockIdentityScheme.Legacy);

    /// <summary>
    /// Derives the CANONICAL identity from an explicit path. The input MUST be fully
    /// qualified; a relative path is REJECTED rather than silently resolved against the
    /// current directory (M1D F-2 — the defect that makes V14..V17 unstable).
    /// </summary>
    public static LockIdentity CanonicalFromWorkbookPath(string workbookPath, bool allowUnc = true)
    {
        ArgumentNullException.ThrowIfNull(workbookPath);
        return Build(NormalizeCanonicalPath(workbookPath, allowUnc), LockIdentityScheme.Canonical);
    }

    /// <summary>
    /// Derives the CANONICAL identity from an opaque store token (a name, not a path).
    /// Path-ness is an explicit parameter here, never a heuristic (M1D F-3 — the defect that
    /// makes V15/V16/V17 read a token as a path).
    /// </summary>
    public static LockIdentity CanonicalFromStoreToken(string token)
    {
        var value = Require(token).Trim();
        if (value.Length == 0)
            throw new StoreIdentityException("A store token must not be empty or whitespace.");
        return Build(value, LockIdentityScheme.Canonical);
    }

    /// <summary>
    /// Both object names a transitional acquirer must contend on while a migration is staged
    /// (LI-7 option (a)). Ordered canonical-first so a caller that takes only the first
    /// element is taking the target identity, never the legacy one by accident.
    /// </summary>
    public static IReadOnlyList<string> InteropObjectNames(string workbookPath, bool allowUnc = true)
    {
        var canonical = CanonicalFromWorkbookPath(workbookPath, allowUnc);
        var legacy = FromWorkbookPath(workbookPath);
        return canonical.ObjectName == legacy.ObjectName
            ? new[] { canonical.ObjectName }
            : new[] { canonical.ObjectName, legacy.ObjectName };
    }

    // ================================================================ normalisation

    /// <summary>
    /// LEGACY normalisation — transcribed verbatim from the shipping implementation. Do not
    /// "improve" this: the 19 frozen vectors and every lock currently held depend on it.
    /// </summary>
    internal static string NormalizeLegacy(string identity)
    {
        var value = identity.Trim();
        var looksLikePath = value.Contains('\\') || value.Contains('/') || Path.HasExtension(value);
        if (!looksLikePath) return value;
        var full = Path.GetFullPath(value).Replace('/', '\\').TrimEnd('\\');
        return OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full;
    }

    /// <summary>
    /// CANONICAL path normalisation. Pure lexical: no CWD, no file system, no clock.
    /// <list type="number">
    /// <item>trim surrounding whitespace;</item>
    /// <item><b>F-2</b> reject anything not fully qualified — a relative path has no stable
    /// identity;</item>
    /// <item><b>UNC</b> preserve the <c>\\server\share</c> head, reject when disallowed;</item>
    /// <item>separators folded to <c>\</c>, duplicate separators collapsed;</item>
    /// <item><c>.</c> segments dropped and <c>..</c> pairs collapsed LEXICALLY, anchored at the
    /// drive root (or the UNC head) so no <c>..</c> can escape above it;</item>
    /// <item><b>F-4</b> a single trailing separator removed, EXCEPT at a root, where it is
    /// preserved (<c>D:\</c> must not degrade to the drive-relative <c>D:</c>);</item>
    /// <item><b>F-5</b> case folded with <see cref="string.ToLowerInvariant"/> on every
    /// platform, so a Windows and a non-Windows consumer of the same store agree.</item>
    /// </list>
    /// </summary>
    internal static string NormalizeCanonicalPath(string path, bool allowUnc)
    {
        var value = Require(path).Trim();
        if (value.Length == 0)
            throw new StoreIdentityException("A store path must not be empty or whitespace.");

        // (4a) separator folding first, so the remaining tests see one spelling.
        value = value.Replace('/', '\\');

        var isUnc = value.StartsWith(@"\\", StringComparison.Ordinal);
        if (isUnc && !allowUnc)
            throw new StoreIdentityException(
                $"UNC store paths are not supported for this derivation: '{path}'. " +
                "A UNC path and its local path are the same store reached two ways (M1D F-1); " +
                "resolving that requires the file system, not a string.");

        if (!isUnc && !IsFullyQualifiedLocal(value))
            throw new StoreIdentityException(
                $"Store path must be fully qualified; '{path}' is relative. A relative path's " +
                "identity depends on the current directory, which is exactly M1D vector V14's defect (F-2).");

        // (4b) collapse duplicate separators, keeping any UNC head intact.
        if (isUnc)
        {
            var body = value[2..];
            value = @"\\" + CollapseSeparators(body);
        }
        else
        {
            value = CollapseSeparators(value);
        }

        // (4c) `.` and `..` collapse, LEXICALLY. This is the step whose absence made
        //       `D:\NEXUS\Products\.\Developer\...` and
        //       `D:\NEXUS\Products\Developer\..\Developer\...` hash differently from
        //       `D:\NEXUS\Products\Developer\...` — three spellings of one store, three lock
        //       names, no contention. It stays purely lexical (no GetFullPath), so the
        //       zero-I/O property above is preserved: the anchor is derived from the string,
        //       never from the file system.
        value = CollapseDotSegments(value, isUnc);

        // (5) trailing separator: keep exactly one at a root, remove it everywhere else.
        var isRoot = IsRoot(value);
        if (!isRoot)
            value = value.TrimEnd('\\');

        // (6) platform-independent case folding.
        return value.ToLowerInvariant();
    }

    /// <summary>
    /// Removes <c>.</c> segments and resolves <c>..</c> against the preceding segment, without
    /// consulting the file system. The result is anchored: for a local path at <c>X:\</c>, for a
    /// UNC path at <c>\\server\share</c>. A <c>..</c> at the anchor is the anchor (which is what
    /// Windows itself does) — dropping it is what keeps <c>D:\..\wb.xlsx</c> and
    /// <c>D:\wb.xlsx</c> from becoming two identities for one file.
    /// <para>Symlinks and 8.3 aliases are NOT resolved here and cannot be: that is F-1's
    /// <see cref="TryResolveFinalPath"/>, which is opt-in precisely because it needs I/O.</para>
    /// </summary>
    private static string CollapseDotSegments(string value, bool isUnc)
    {
        string anchor;
        string rest;

        if (isUnc)
        {
            // `\\server\share` is the anchor; nothing may pop above the share.
            var body = value[2..];
            var parts = body.Split('\\');
            var server = parts.Length > 0 ? parts[0] : string.Empty;
            var share = parts.Length > 1 ? parts[1] : string.Empty;
            anchor = @"\\" + server;
            if (share.Length > 0) anchor += "\\" + share;
            rest = parts.Length > 2 ? string.Join('\\', parts[2..]) : string.Empty;
        }
        else
        {
            anchor = value[..3];          // `X:\`
            rest = value.Length > 3 ? value[3..] : string.Empty;
        }

        var kept = new List<string>();
        foreach (var seg in rest.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (seg == ".") continue;
            if (seg == "..")
            {
                if (kept.Count > 0) kept.RemoveAt(kept.Count - 1);
                continue;
            }
            kept.Add(seg);
        }

        // The local anchor already ends in a separator (`D:\`) and the UNC anchor does not
        // (`\\server\share`), so the join must not assume either. Appending unconditionally
        // produced `d:\\nexus\...` — a normalisation that was wrong but CONSISTENT, which is
        // exactly the shape of defect that makes every convergence assertion pass while the
        // derived name no longer matches the one shipping. The duplicate-separator assertion
        // is what caught it.
        if (kept.Count == 0) return anchor;
        return anchor.TrimEnd('\\') + "\\" + string.Join('\\', kept);
    }

    private static string CollapseSeparators(string s)
    {
        var sb = new StringBuilder(s.Length);
        var lastWasSep = false;
        foreach (var c in s)
        {
            var isSep = c == '\\';
            if (isSep && lastWasSep) continue;
            sb.Append(c);
            lastWasSep = isSep;
        }
        return sb.ToString();
    }

    /// <summary>True for <c>C:\</c> or <c>C:</c> shaped input, optionally with trailing separator.</summary>
    private static bool IsRoot(string value)
    {
        if (value.Length == 2 && char.IsLetter(value[0]) && value[1] == ':') return true;
        if (value.Length == 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '\\') return true;
        return false;
    }

    /// <summary>
    /// Fully-qualified, without <see cref="Path.GetFullPath(string)"/> — which would consult
    /// the current directory and reintroduce the dependency being removed.
    /// </summary>
    private static bool IsFullyQualifiedLocal(string value) =>
        value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '\\';

    // ================================================================ hashing

    private static LockIdentity Build(string normalized, LockIdentityScheme scheme)
    {
        var hashHex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return new LockIdentity(normalized, hashHex, scheme);
    }

    private static string Require(string identity) =>
        string.IsNullOrWhiteSpace(identity)
            ? throw new StoreIdentityException("A store identity is required to derive a lock identity.")
            : identity;

    // ================================================================ F-1, opt-in, I/O

    /// <summary>
    /// Resolves a path to the single canonical path the file system itself reports, which is
    /// the only way to make <c>\\server\share\x</c> and <c>D:\x</c>, or <c>PROGRA~1</c> and
    /// <c>Program Files</c>, agree (M1D F-1).
    /// <para><b>This is the only member of this type that touches the file system and it is
    /// never called implicitly.</b> A BOOTSTRAP_SAFE caller must derive identities with Nexus
    /// down; an implicit call here would make identity derivation fail exactly when it is
    /// most needed. Callers that can afford I/O opt in, explicitly.</para>
    /// <para>Returns false when the path does not exist or the platform does not support it —
    /// the caller then keeps the lexical identity. Resolution failing is not an error; it is
    /// the absence of an improvement.</para>
    /// </summary>
    public static bool TryResolveFinalPath(string path, out string resolved)
    {
        resolved = path;
        if (!OperatingSystem.IsWindows()) return false;
        if (string.IsNullOrWhiteSpace(path)) return false;

        IntPtr handle = CreateFileW(
            path.Trim(), 0, FileShareRead | FileShareWrite | FileShareDelete, IntPtr.Zero,
            OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle == InvalidHandleValue) return false;

        try
        {
            var buffer = new StringBuilder(1024);
            uint len = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, VolumeNameDos);
            if (len == 0 || len >= buffer.Capacity) return false;

            var final = buffer.ToString();
            // The API returns the \\?\ form; strip it so the result re-enters lexical
            // normalisation as an ordinary fully-qualified path.
            if (final.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
                final = @"\\" + final[8..];
            else if (final.StartsWith(@"\\?\", StringComparison.Ordinal))
                final = final[4..];

            resolved = final;
            return true;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint FileShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint VolumeNameDos = 0;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateFileW(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(IntPtr hFile, StringBuilder lpszFilePath, uint cchFilePath, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);
}
