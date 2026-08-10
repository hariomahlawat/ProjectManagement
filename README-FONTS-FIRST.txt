PRISM PUBLICATION FONTS — READY-TO-PASTE SETUP
================================================

IMPORTANT
---------
This package intentionally does NOT contain font binary files.

Use Install-PrismPublicationFonts.ps1 on an internet-connected development
machine. It downloads the approved static font files directly from the
official Google Fonts repositories into the exact folders expected by PRISM.

FILES CREATED
-------------
wwwroot\fonts\publications\dm-sans\
  DMSans-Regular.ttf
  DMSans-Medium.ttf
  DMSans-SemiBold.ttf
  DMSans-Bold.ttf
  DMSans-Italic.ttf
  DMSans-BoldItalic.ttf
  OFL.txt

wwwroot\fonts\publications\alatsi\
  Alatsi-Regular.ttf
  OFL.txt

HOW TO RUN
----------
From the ProjectManagement project root:

PowerShell:
  Set-ExecutionPolicy -Scope Process Bypass
  .\tools\Install-PrismPublicationFonts.ps1

If you are running the script from another folder:

  .\tools\Install-PrismPublicationFonts.ps1 `
      -ProjectRoot "E:\Dot Net Web Development\ProjectManagement"

To replace files already present:

  .\tools\Install-PrismPublicationFonts.ps1 -Force

VERIFY
------
  .\tools\Test-PrismPublicationFonts.ps1

OFFLINE / AIR-GAPPED DEPLOYMENT
-------------------------------
Run the installer only on an internet-connected development machine.

After the files are downloaded:
1. Keep the TTF and OFL.txt files inside the PRISM project.
2. Publish PRISM normally.
3. Verify that wwwroot\fonts\publications is present in the published output.
4. Transfer the published application to the air-gapped environment.
5. Restart the PRISM application / IIS application pool.

No Windows font installation is required.

PRISM EXPECTATION
-----------------
The Brochure subsystem's PublicationFontRegistry expects these exact static
DM Sans filenames and Alatsi-Regular.ttf. If DM Sans is absent, the renderer
falls back to QuestPDF's bundled Lato family. If Alatsi is absent, Cover A
uses the primary publication family instead.
