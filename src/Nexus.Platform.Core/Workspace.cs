// M-08-1.4 red-demo: DELIBERATE boundary violation - do not merge.
// A product-domain concept ("Workspace") leaking into the Platform layer.
// The architecture gate Nexus.Platform.Architecture.Tests.PlatformBoundaryTests
// .Platform_MustNotContain_ProductTypeNames forbids this type name in a
// Platform assembly. This file exists only to prove the gate turns the build
// red; it is reverted immediately after the demonstration.
namespace Nexus.Platform.Core;

public sealed class Workspace
{
}
