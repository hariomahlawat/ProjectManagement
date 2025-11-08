# Implement production-grade full-text search (PostgreSQL) for Document Repository

Team,

We now have OCR working and we want the extracted text to become searchable. Let’s implement FTS in PostgreSQL in a way that will scale as document count and OCR size grow.

## 1. Data model – where to store OCR text

**Do this (recommended):**

- Create a **separate table** to hold large OCR text, e.g. `docrepo_document_texts` (name as per our conventions).
- 1–1 with the main document table (FK to document id, PK = document id).
- Columns:
  - `document_id` (PK, FK)
  - `ocr_text` (text, nullable)
  - `search_vector` (tsvector) – optional here if we want to keep FTS on this table
  - `updated_at`

**Why separate table (better & scalable):**

1. OCR text can be **very large**, and mixing it into the main documents table will bloat that table and slow scans and index maintenance.
2. Many list views only need document metadata (subject, office, category). Keeping the main table lean makes those queries faster.
3. We can index / vacuum the text table independently.
4. If later we want to move only OCR text to cheaper storage or partition it, it’s isolated.

So: **don’t put big OCR text on the main document row**; keep it in a sibling table.

## 2. What goes into search

We want search to consider:

1. **High weight (A):**
   - document subject/title
   - sender / received-from
   - maybe tags (names)
2. **Medium weight (B/C):**
   - office / document category names (if we denormalize them)
3. **Low weight (D):**
   - OCR text

That way, a subject match always beats a random word deep in the PDF.

## 3. Where to build the tsvector

We have two good patterns; let’s pick the clearer one:

**Pattern we should implement now:**

- Keep “business” columns where they are (subject, sender, etc.) in the main table.
- Keep **large OCR text** in `docrepo_document_texts`.
- Create a **Postgres view or a trigger-based tsvector on the main table** that *pulls in* OCR text from the text table.
  - Easiest: store a `search_vector` column on the **main** document table and let a trigger update it by reading OCR text from the sibling table.
  - Trigger fires on:
    - insert/update of document
    - insert/update of document_texts
  - This keeps searching simple: the app can query only one table.

This gives us a single GIN index to query.

## 4. DB changes (PostgreSQL)

1. **New table** `docrepo_document_texts` as described above.
2. **Add column** to main document table: `search_vector tsvector`.
3. **Create trigger function** (plpgsql) that:
   - pulls subject, sender, tags (if denormalized) from the document row
   - pulls ocr_text from `docrepo_document_texts` (LEFT JOIN inside the function)
   - runs `setweight(...)` for each part
   - assigns to `NEW.search_vector`
4. **Create GIN index** on the document table:

   ```sql
   CREATE INDEX idx_docrepo_documents_search
     ON docrepo_documents
     USING GIN (search_vector);
   ```

5. If tags are in a separate table, two options:
   - denormalize tag names into a `tags_text` column on the doc table and let the trigger use it, **or**
   - change the trigger to re-aggregate tags when tags change. (Slightly more work; denormalizing is simpler.)

## 5. App changes

1. **OCR pipeline step**: when OCR finishes, save text to `docrepo_document_texts` for that document. That write will trigger the FTS update (via DB trigger).
2. **Search endpoint/page** (`IndexModel`):
   - Right now we do `ILIKE` + filters.
   - Change to:
     - Build a tsquery from the user input (`plainto_tsquery('simple', @q)` or language we pick).
     - Filter: `WHERE search_vector @@ to_tsquery(...)`
     - Keep existing filters for office, document category, year, active.
     - Order by `ts_rank_cd(search_vector, to_tsquery(...)) DESC, document_date DESC, created_at DESC`.
3. **Paging** stays as-is.

## 6. Config / language

- Pick a consistent text search config (likely `english` unless we need something else; if we have mixed languages, we’ll revisit).
- Make it a DB-level default or put it in the trigger so the app doesn’t have to decide.

## 7. Future-proofing for Elasticsearch

- Wrap the search logic in something like `IDocumentSearchService`.
- For now the implementation uses EF + Postgres FTS.
- If we outgrow PG or need fuzzy/suggestions, we can add an Elasticsearch implementation and switch.

## Summary for devs

- ✅ Storage paths cleanup done.
- ➡️ Now: add `docrepo_document_texts` (1–1), store OCR there.
- ➡️ Add `search_vector` on main docs, updated by a trigger that combines metadata + OCR text with weights.
- ➡️ Add GIN index.
- ➡️ Update Razor page to query `search_vector` + filters.
- ➡️ Keep code behind an interface so we can swap engines later.
