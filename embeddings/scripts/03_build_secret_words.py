"""
Step 3: Curate a "good secret words" pool from the filtered vocabulary.

Uses WordNet lexicographer categories (lexnames) as a semi-automated proxy for
"concrete, guessable noun": animals, food, man-made objects, plants, places,
body parts, and professions. This is not perfect (WordNet sense disambiguation
is approximate — we just check whether ANY noun sense of the word falls in an
allowed category) but is a reasonable v1 filter per the spec, and can be
refined manually later by editing data/secret_words.json directly.

Output: embeddings/data/secret_words.json
  [{"word": str, "length": int, "category": str}, ...]
"""
import json
from pathlib import Path

import nltk
from nltk.corpus import stopwords
from nltk.corpus import wordnet as wn

MIN_LEN = 4
MAX_LEN = 10  # hard cap per spec, even though vocab allows longer words
TARGET_COUNT = 400

# Map WordNet noun lexnames -> our category labels.
ALLOWED_LEXNAMES = {
    "noun.animal": "animal",
    "noun.food": "food",
    "noun.plant": "plant",
    "noun.artifact": "object",
    "noun.body": "body_part",
    "noun.location": "place",
    "noun.person": "profession",
}

# Common WordNet noise words worth excluding even if they slip through
# (overly abstract senses, units, etc. — refine manually as needed).
DENYLIST = {
    "thing", "object", "stuff", "matter", "unit", "part", "piece", "type",
    "kind", "form", "body", "being", "entity", "item",
    "state", "south", "north", "east", "west", "back", "national", "front",
    "case", "point", "level", "area", "side", "end", "top", "bottom",
}

ROOT = Path(__file__).resolve().parent.parent
DATA_DIR = ROOT / "data"


def ensure_nltk_data() -> None:
    try:
        nltk.data.find("corpora/wordnet")
    except LookupError:
        nltk.download("wordnet")
    try:
        nltk.data.find("corpora/omw-1.4")
    except LookupError:
        nltk.download("omw-1.4")
    try:
        nltk.data.find("corpora/stopwords")
    except LookupError:
        nltk.download("stopwords")


def categorize(word: str) -> str | None:
    # Only look at the word's single most common sense overall (WordNet orders
    # synsets by frequency for a given lemma). This avoids picking common
    # function/verb words (e.g. "have") that happen to carry an obscure noun
    # sense — we only want words whose PRIMARY meaning is a concrete noun.
    all_synsets = wn.synsets(word)
    if not all_synsets:
        return None
    primary = all_synsets[0]
    if primary.pos() != "n":
        return None
    return ALLOWED_LEXNAMES.get(primary.lexname())


def main() -> None:
    ensure_nltk_data()

    vocab_path = DATA_DIR / "vocab_words.json"
    if not vocab_path.exists():
        raise SystemExit(f"Missing {vocab_path}. Run 02_build_vocab.py first.")

    with open(vocab_path, "r", encoding="utf-8") as f:
        vocab = json.load(f)

    stop_words = set(stopwords.words("english"))

    candidates = []
    for entry in vocab:
        word = entry["word"]
        length = entry["length"]
        if not (MIN_LEN <= length <= MAX_LEN):
            continue
        if word in DENYLIST or word in stop_words:
            continue
        category = categorize(word)
        if category is None:
            continue
        candidates.append({"word": word, "length": length, "category": category})

    # Vocab is already frequency-ordered (from step 2). Cap how many words we
    # take per category so common categories (e.g. "object") don't crowd out
    # rarer ones (e.g. "food", "animal"), while still favoring common words
    # within each category.
    per_category_cap = max(40, TARGET_COUNT // len(ALLOWED_LEXNAMES))
    category_counts: dict[str, int] = {}
    selected = []
    for c in candidates:
        if len(selected) >= TARGET_COUNT:
            break
        if category_counts.get(c["category"], 0) >= per_category_cap:
            continue
        category_counts[c["category"]] = category_counts.get(c["category"], 0) + 1
        selected.append(c)

    out_path = DATA_DIR / "secret_words.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(selected, f, ensure_ascii=False, indent=2)

    by_cat: dict[str, int] = {}
    for c in selected:
        by_cat[c["category"]] = by_cat.get(c["category"], 0) + 1

    print(f"Selected {len(selected)} secret-word candidates (of {len(candidates)} matched).")
    print("By category:", by_cat)
    print(f"Wrote {out_path}")


if __name__ == "__main__":
    main()
