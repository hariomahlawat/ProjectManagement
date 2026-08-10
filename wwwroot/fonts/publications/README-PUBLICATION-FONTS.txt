PRISM PUBLICATION FONTS — OFFLINE DEPLOYMENT
============================================

The brochure generator is fully offline. It never requests a web font.

Preferred brochure family
-------------------------
Copy the licensed STATIC TTF files for DM Sans to:
  wwwroot/fonts/publications/dm-sans/

Expected filenames:
  DMSans-Regular.ttf
  DMSans-Medium.ttf
  DMSans-SemiBold.ttf
  DMSans-Bold.ttf
  DMSans-Italic.ttf
  DMSans-BoldItalic.ttf

Optional Cover A display accent
-------------------------------
Copy the licensed STATIC TTF for Alatsi to:
  wwwroot/fonts/publications/alatsi/Alatsi-Regular.ttf

Licensing
---------
Keep the applicable font licence text with your deployed font package. Use only font files
that your organisation is authorised to redistribute/deploy. Variable font files are not
used by this implementation.

Safe fallback
-------------
If the DM Sans files have not yet been installed, the brochure remains functional and uses
QuestPDF's Lato family. If Alatsi is absent, Cover A uses DM Sans (or Lato fallback) rather
than failing generation.

No font binary is required for the source-code replacement itself. Restart the application
after adding or changing the font package so the process registers the new files cleanly.
