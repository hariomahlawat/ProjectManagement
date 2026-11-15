using System;
using System.ComponentModel.DataAnnotations;

namespace ProjectManagement.Models
{
    /// <summary>
    /// A crest or logo an administrator uploaded for everyone to put on a collage, in Collage
    /// Studio's "Crests and logos" group. The picture is PRISM's own PNG, re-encoded from the
    /// upload; nothing the uploader sent is kept. A retired sticker is hidden from the picker but
    /// still served, so drafts that use it keep their crest.
    /// </summary>
    public class CollageSticker
    {
        public const int NameMaxLength = 40;
        public const int SearchWordsMaxLength = 200;

        [Key]
        public int Id { get; set; }

        [Required, StringLength(NameMaxLength)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Extra words the sticker is found by, such as the unit's other names.</summary>
        [StringLength(SearchWordsMaxLength)]
        public string? SearchWords { get; set; }

        /// <summary>The PNG under the upload root, as a relative storage key.</summary>
        [Required, StringLength(260)]
        public string StorageKey { get; set; } = string.Empty;

        public int Width { get; set; }

        public int Height { get; set; }

        /// <summary>Changes whenever the picture changes, so browsers fetch it afresh.</summary>
        public int Version { get; set; } = 1;

        /// <summary>The place in the picker: lower first.</summary>
        public int SortOrder { get; set; }

        public bool IsRetired { get; set; }

        [StringLength(450)]
        public string? CreatedByUserId { get; set; }

        public DateTimeOffset CreatedAtUtc { get; set; }

        public DateTimeOffset UpdatedAtUtc { get; set; }
    }
}
