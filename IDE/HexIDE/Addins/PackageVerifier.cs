namespace HexIDE.Addins;

/// <summary>
/// The IDE's package verifier: supplies the baked-in HexIDE root public key and the pinned first-party
/// publisher public key (both from <see cref="TrustAnchors"/>) to the pure <see cref="PackageVerification"/>
/// logic. Replacing <c>hexide-root.pub</c> rotates the trust root; replacing <c>hexide-firstparty.pub</c>
/// rotates the first-party identity.
/// </summary>
internal sealed class PackageVerifier : IPackageVerifier
{
    public PackageVerificationResult Verify(string packageDirectory) =>
        PackageVerification.Verify(packageDirectory, TrustAnchors.RootPublicKey, TrustAnchors.FirstPartyPublicKey);
}
