namespace ProjectManagement.Services.Publications;

/// <summary>
/// Single source of truth for curated offline Cover A artwork. Every shipped artwork is a complete
/// approved hero with the institutional identity already embedded. Renderers must not place a
/// second set of organisation marks over these assets.
/// </summary>
public static class BrochureInstitutionalCoverArtworkCatalog
{
    public static BrochureInstitutionalArtworkIdentityMode IdentityMode(
        BrochureInstitutionalCoverArtwork artwork)
        => artwork switch
        {
            BrochureInstitutionalCoverArtwork.ReferenceOriginal => BrochureInstitutionalArtworkIdentityMode.FullArtwork,
            BrochureInstitutionalCoverArtwork.PremiumGreenGold => BrochureInstitutionalArtworkIdentityMode.FullArtwork,
            BrochureInstitutionalCoverArtwork.CinematicCyber => BrochureInstitutionalArtworkIdentityMode.FullArtwork,
            BrochureInstitutionalCoverArtwork.ExecutiveTeal => BrochureInstitutionalArtworkIdentityMode.FullArtwork,
            BrochureInstitutionalCoverArtwork.LuminousHalo => BrochureInstitutionalArtworkIdentityMode.FullArtwork,
            _ => throw new ArgumentOutOfRangeException(nameof(artwork), artwork, "Unknown institutional cover artwork.")
        };

    public static string RelativePath(BrochureInstitutionalCoverArtwork artwork)
        => artwork switch
        {
            BrochureInstitutionalCoverArtwork.PremiumGreenGold => "img/publications/covers/cover-a-premium-green-gold.jpg",
            BrochureInstitutionalCoverArtwork.CinematicCyber => "img/publications/covers/cover-a-cinematic-cyber.jpg",
            BrochureInstitutionalCoverArtwork.ExecutiveTeal => "img/publications/covers/cover-a-executive-teal.jpg",
            BrochureInstitutionalCoverArtwork.LuminousHalo => "img/publications/covers/cover-a-luminous-halo.jpg",
            _ => "img/publications/covers/cover-a-reference-original.jpg"
        };
}
