"""
Step 1: Download and extract GloVe 6B embeddings (Stanford NLP).

Downloads glove.6B.zip (~822MB, contains 50d/100d/200d/300d vectors trained on
6B tokens of Wikipedia + Gigaword) and extracts only the 100-dimensional file,
which is a good size/quality tradeoff for a word-guessing game.

Run once. Output: embeddings/data/raw/glove.6B.100d.txt
"""
import zipfile
from pathlib import Path

import requests
from tqdm import tqdm

GLOVE_URL = "https://nlp.stanford.edu/data/glove.6B.zip"
DIM = 100
TARGET_MEMBER = f"glove.6B.{DIM}d.txt"

ROOT = Path(__file__).resolve().parent.parent
RAW_DIR = ROOT / "data" / "raw"
ZIP_PATH = RAW_DIR / "glove.6B.zip"
OUT_PATH = RAW_DIR / TARGET_MEMBER


def download(url: str, dest: Path) -> None:
    dest.parent.mkdir(parents=True, exist_ok=True)
    with requests.get(url, stream=True, timeout=60) as r:
        r.raise_for_status()
        total = int(r.headers.get("content-length", 0))
        with open(dest, "wb") as f, tqdm(
            total=total, unit="B", unit_scale=True, desc=dest.name
        ) as bar:
            for chunk in r.iter_content(chunk_size=1024 * 256):
                f.write(chunk)
                bar.update(len(chunk))


def main() -> None:
    if OUT_PATH.exists():
        print(f"Already have {OUT_PATH}, skipping download.")
        return

    if not ZIP_PATH.exists():
        print(f"Downloading {GLOVE_URL} -> {ZIP_PATH}")
        download(GLOVE_URL, ZIP_PATH)
    else:
        print(f"Zip already downloaded at {ZIP_PATH}")

    print(f"Extracting {TARGET_MEMBER} ...")
    with zipfile.ZipFile(ZIP_PATH) as zf:
        with zf.open(TARGET_MEMBER) as src, open(OUT_PATH, "wb") as dst:
            dst.write(src.read())

    print(f"Done: {OUT_PATH}")
    print("You can delete the zip file to save disk space if desired:")
    print(f"  {ZIP_PATH}")


if __name__ == "__main__":
    main()
