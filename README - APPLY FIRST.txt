ARPP Word Builder CS0133 Fix
=============================

Replace:
Services\Reports\ArppFyProjectUpdate\ArppFyProjectUpdateWordBuilder.cs

Root cause:
TableWidth is declared as static readonly because it is calculated from Widths.Sum().
A local const therefore cannot be initialized from TableWidth:

    const int rightWidth = TableWidth - leftWidth;

Fix:
Use a normal local variable:

    var rightWidth = TableWidth - leftWidth;

This preserves the calculated footer geometry and avoids hard-coding a duplicate table width.

No other report logic, formatting, DI, database, or export behavior is changed.
