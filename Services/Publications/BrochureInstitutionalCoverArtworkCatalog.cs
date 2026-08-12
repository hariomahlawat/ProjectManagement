namespace ProjectManagement.Services.Publications;

/// <summary>
/// Single source of truth for curated offline Cover A artwork. Reference Original is a complete
/// approved hero and therefore must never receive institutional overlays. Generated alternatives
/// are deliberately background-only artwork; PRISM overlays exact official marks at render time.
/// </summary>
public static class BrochureInstitutionalCoverArtworkCatalog
{
    public static BrochureInstitutionalArtworkIdentityMode IdentityMode(
        BrochureInstitutionalCoverArtwork artwork)
        => artwork == BrochureInstitutionalCoverArtwork.ReferenceOriginal
            ? BrochureInstitutionalArtworkIdentityMode.FullArtwork
            : BrochureInstitutionalArtworkIdentityMode.BackgroundOnly;

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
