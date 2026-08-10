PRISM PUBLICATION FONTS — OFFLINE DEPLOYMENT
============================================

The brochure subsystem never requests a web font.

Current supported deployment path (recommended with supplied installer)
-----------------------------------------------------------------------
If you already used Install-PrismPublicationFonts.ps1, no move is required:

wwwroot/fonts/publications/dm-sans/
  DMSans-Regular.ttf
  DMSans-Medium.ttf
  DMSans-SemiBold.ttf
  DMSans-Bold.ttf
  DMSans-Italic.ttf
  DMSans-BoldItalic.ttf

wwwroot/fonts/publications/alatsi/
  Alatsi-Regular.ttf

Optional hardened server-resource path
--------------------------------------
PRISM also recognises:

Resources/Publications/Fonts/dm-sans/
Resources/Publications/Fonts/alatsi/

Use this path only when your publish/deployment process explicitly includes the Resources
font folder. The existing wwwroot path requires no project-file change and remains fully
supported for air-gapped deployment.

Startup registration
--------------------
The application registers the publication font package once during startup. Restart PRISM /
the IIS application pool after adding or replacing any font files.

Fallback
--------
If DM Sans is unavailable or cannot be registered, QuestPDF's bundled Lato remains the safe
fallback. If Alatsi is unavailable, Cover A uses the primary publication family.

Licensing
---------
Keep the applicable OFL/licence text with the deployed font package. Use static TTF files,
not variable-font files.
