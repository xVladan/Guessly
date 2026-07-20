"""
Step 4: Sanity check — load the exported vocab/vectors and time a full
cosine-similarity rank computation for a sample secret word, the same
operation the .NET backend will perform once per round.
"""
import json
import time
from pathlib import Path

import numpy as np

ROOT = Path(__file__).resolve().parent.parent
DATA_DIR = ROOT / "data"


def load() -> tuple[list[str], np.ndarray, dict]:
    with open(DATA_DIR / "meta.json", "r", encoding="utf-8") as f:
        meta = json.load(f)
    with open(DATA_DIR / "vocab_words.json", "r", encoding="utf-8") as f:
        words = [e["word"] for e in json.load(f)]
    vectors = np.fromfile(DATA_DIR / "vocab_vectors.f32", dtype=np.float32)
    vectors = vectors.reshape(meta["count"], meta["dim"])
    return words, vectors, meta


def rank_for(word: str, words: list[str], vectors: np.ndarray) -> list[str]:
    idx = words.index(word)
    target = vectors[idx]

    norms = np.linalg.norm(vectors, axis=1)
    target_norm = np.linalg.norm(target)
    sims = (vectors @ target) / (norms * target_norm + 1e-8)

    order = np.argsort(-sims)
    return [words[i] for i in order]


def main() -> None:
    words, vectors, meta = load()
    print(f"Loaded {meta['count']} words, dim={meta['dim']}")

    with open(DATA_DIR / "secret_words.json", "r", encoding="utf-8") as f:
        secret_words = json.load(f)

    sample_word = secret_words[0]["word"] if secret_words else "apple"
    print(f"Computing full rank list for '{sample_word}' ...")

    start = time.perf_counter()
    ranked = rank_for(sample_word, words, vectors)
    elapsed = time.perf_counter() - start

    print(f"Done in {elapsed * 1000:.1f} ms")
    print(f"Rank 1 (should be secret word itself): {ranked[0]}")
    print(f"Top 10 closest: {ranked[:10]}")


if __name__ == "__main__":
    main()
