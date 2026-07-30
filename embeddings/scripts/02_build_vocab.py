"""
Step 2: Build the filtered "playable" vocabulary from raw GloVe vectors.

GloVe's glove.6B.*.txt files are ordered by descending corpus frequency, which
we use as a cheap frequency filter. We further restrict to tokens that appear
in NLTK's English dictionary word list (mostly excludes proper nouns, numbers,
misspellings and noise tokens like "n't" or "co.").

Outputs (embeddings/data/):
  - vocab_words.json   [{"word": str, "length": int}, ...]  index-aligned with vectors
  - vocab_vectors.f32   raw float32 binary, shape (N, DIM), row-major, little-endian
  - meta.json           {"dim": DIM, "count": N, "source": "..."}
"""
import json
import struct
from pathlib import Path

import nltk
import numpy as np

DIM = 300  # 300d captures semantic similarity noticeably better than 100d — worth the extra size.
MAX_VOCAB = 40_000
MIN_WORD_LEN = 2
MAX_WORD_LEN = 12  # generous upper bound; per-round length filter is applied at query time

ROOT = Path(__file__).resolve().parent.parent
RAW_PATH = ROOT / "data" / "raw" / f"glove.6B.{DIM}d.txt"
OUT_DIR = ROOT / "data"


def ensure_nltk_data() -> None:
    for pkg, path in [("words", "corpora/words"), ("stopwords", "corpora/stopwords")]:
        try:
            nltk.data.find(path)
        except LookupError:
            nltk.download(pkg)


def load_dictionary_words() -> set[str]:
    from nltk.corpus import words as nltk_words

    return {w.lower() for w in nltk_words.words()}


def is_playable(token: str, dictionary: set[str]) -> bool:
    if not token.isalpha():
        return False
    if not (MIN_WORD_LEN <= len(token) <= MAX_WORD_LEN):
        return False
    if token not in dictionary:
        return False
    return True


def main() -> None:
    if not RAW_PATH.exists():
        raise SystemExit(f"Missing {RAW_PATH}. Run 01_download_glove.py first.")

    ensure_nltk_data()
    dictionary = load_dictionary_words()
    print(f"Loaded {len(dictionary)} dictionary words from NLTK.")

    words: list[str] = []
    vectors: list[np.ndarray] = []

    with open(RAW_PATH, "r", encoding="utf-8") as f:
        for line in f:
            if len(words) >= MAX_VOCAB:
                break
            parts = line.rstrip("\n").split(" ")
            token = parts[0]
            if not is_playable(token, dictionary):
                continue
            vec = np.asarray(parts[1:], dtype=np.float32)
            if vec.shape[0] != DIM:
                continue
            words.append(token)
            vectors.append(vec)

    print(f"Kept {len(words)} playable words (target was up to {MAX_VOCAB}).")

    matrix = np.vstack(vectors).astype(np.float32)

    OUT_DIR.mkdir(parents=True, exist_ok=True)

    with open(OUT_DIR / "vocab_vectors.f32", "wb") as f:
        f.write(matrix.tobytes(order="C"))

    vocab_json = [{"word": w, "length": len(w)} for w in words]
    with open(OUT_DIR / "vocab_words.json", "w", encoding="utf-8") as f:
        json.dump(vocab_json, f, ensure_ascii=False)

    meta = {
        "dim": DIM,
        "count": len(words),
        "source": f"glove.6B.{DIM}d",
        "vectorFile": "vocab_vectors.f32",
        "wordsFile": "vocab_words.json",
        "byteOrder": "little-endian",
        "dtype": "float32",
    }
    with open(OUT_DIR / "meta.json", "w", encoding="utf-8") as f:
        json.dump(meta, f, indent=2)

    print(f"Wrote vocab_words.json, vocab_vectors.f32 ({matrix.nbytes / 1e6:.1f} MB), meta.json")


if __name__ == "__main__":
    main()
