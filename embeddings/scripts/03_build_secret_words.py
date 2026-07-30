"""
Step 3: Build the "good secret words" pool from a hand-picked list of common,
concrete, unambiguous nouns.

An earlier version of this script derived candidates automatically from
WordNet noun categories. In practice that produced too much noise for a
guessing game — proper nouns (country/city names GloVe's news corpus treats
as common tokens, e.g. "china", "paris", "texas"), person names picked up as
nouns ("murphy", "clinton", "chen"), and adjectives/verbs miscategorized as
nouns ("quick", "come", "generic"). Real playtesting confirmed this was a
recurring complaint, so this step is now a curated whitelist instead: every
word below was chosen by hand as a clear, everyday, guessable noun. The list
is intersected with the actual filtered vocabulary (so every secret word is
guaranteed to have a real embedding) and can be edited directly to add/remove
words — no need to re-run any NLP heuristic.

Output: embeddings/data/secret_words.json
  [{"word": str, "length": int, "category": str}, ...]
"""
import json
from pathlib import Path

MIN_LEN = 4
MAX_LEN = 10  # hard cap per spec, even though vocab allows longer words

ROOT = Path(__file__).resolve().parent.parent
DATA_DIR = ROOT / "data"

# (word, category) — category is just metadata, not shown to players.
CURATED_WORDS: list[tuple[str, str]] = [
    # animals
    *[(w, "animal") for w in [
        "dog", "cat", "horse", "cow", "pig", "sheep", "goat", "chicken", "duck", "rabbit",
        "mouse", "rat", "fox", "wolf", "bear", "lion", "tiger", "elephant", "monkey", "giraffe",
        "zebra", "kangaroo", "koala", "panda", "deer", "moose", "squirrel", "hedgehog", "bat",
        "whale", "dolphin", "shark", "octopus", "crab", "lobster", "shrimp", "snail", "frog",
        "turtle", "lizard", "snake", "spider", "bee", "ant", "butterfly", "moth", "beetle",
        "eagle", "owl", "parrot", "penguin", "peacock", "swan", "dove", "crow", "sparrow",
        "hawk", "falcon", "camel", "donkey", "buffalo", "hippo", "rhino", "gorilla", "leopard",
        "cheetah", "jaguar", "panther", "otter", "beaver", "raccoon", "skunk", "weasel",
        "ferret", "hamster", "gecko", "iguana", "crocodile", "alligator", "jellyfish",
        "starfish", "seahorse", "stingray", "walrus", "seal",
    ]],
    # food
    *[(w, "food") for w in [
        "apple", "banana", "orange", "grape", "lemon", "lime", "cherry", "peach", "plum",
        "mango", "pineapple", "watermelon", "strawberry", "blueberry", "raspberry", "coconut",
        "avocado", "potato", "tomato", "carrot", "onion", "garlic", "pepper", "cucumber",
        "lettuce", "cabbage", "broccoli", "spinach", "pumpkin", "mushroom", "bread", "cheese",
        "butter", "cream", "yogurt", "honey", "sugar", "bacon", "sausage", "pasta", "pizza",
        "burger", "sandwich", "soup", "salad", "cake", "cookie", "chocolate", "candy",
        "popcorn", "pancake", "waffle", "donut", "cereal", "coffee", "juice",
    ]],
    # everyday objects
    *[(w, "object") for w in [
        "table", "chair", "sofa", "pillow", "blanket", "mirror", "clock", "window", "phone",
        "laptop", "camera", "television", "guitar", "piano", "violin", "pencil", "scissors",
        "spoon", "bottle", "basket", "backpack", "umbrella", "wallet", "glasses", "watch",
        "necklace", "jacket", "scarf", "hammer", "bucket", "candle", "ladder", "wrench",
        "bicycle", "airplane", "helicopter", "balloon", "kite", "puzzle", "mirror",
        "keyboard", "wheel", "engine", "rocket", "anchor", "compass", "telescope", "microscope",
        "battery", "magnet", "shovel", "axe",
    ]],
    # places
    *[(w, "place") for w in [
        "kitchen", "bedroom", "garden", "garage", "school", "hospital", "library", "museum",
        "church", "castle", "farm", "forest", "desert", "jungle", "mountain", "valley",
        "island", "beach", "ocean", "river", "lake", "waterfall", "cave", "volcano", "village",
        "market", "restaurant", "hotel", "airport", "stadium", "playground", "harbor",
        "bridge", "lighthouse", "windmill", "cottage",
    ]],
    # professions
    *[(w, "profession") for w in [
        "doctor", "nurse", "teacher", "farmer", "waiter", "pilot", "driver", "soldier",
        "sailor", "dentist", "lawyer", "engineer", "scientist", "painter", "musician",
        "singer", "dancer", "writer", "photographer", "plumber", "electrician", "carpenter",
        "mechanic", "barber", "tailor", "butcher", "baker", "fisherman", "shepherd",
        "gardener", "librarian", "cashier", "referee",
    ]],
    # body parts
    *[(w, "body_part") for w in [
        "head", "hair", "eye", "ear", "nose", "mouth", "lip", "tooth", "tongue", "chin",
        "cheek", "neck", "shoulder", "elbow", "wrist", "finger", "thumb", "chest", "stomach",
        "waist", "knee", "ankle", "foot", "heel", "skin", "bone", "muscle", "heart", "lung",
        "brain", "eyebrow", "eyelash",
    ]],
    # plants
    *[(w, "plant") for w in [
        "tree", "flower", "rose", "tulip", "daisy", "sunflower", "cactus", "bush", "vine",
        "root", "branch", "moss", "fern", "bamboo", "palm", "willow", "cedar", "mushroom",
        "clover", "ivy",
    ]],
]


def main() -> None:
    vocab_path = DATA_DIR / "vocab_words.json"
    if not vocab_path.exists():
        raise SystemExit(f"Missing {vocab_path}. Run 02_build_vocab.py first.")

    with open(vocab_path, "r", encoding="utf-8") as f:
        vocab_by_word = {e["word"]: e["length"] for e in json.load(f)}

    seen: set[str] = set()
    selected = []
    missing = []
    for word, category in CURATED_WORDS:
        if word in seen:
            continue
        seen.add(word)

        length = vocab_by_word.get(word)
        if length is None:
            missing.append(word)
            continue
        if not (MIN_LEN <= length <= MAX_LEN):
            continue

        selected.append({"word": word, "length": length, "category": category})

    out_path = DATA_DIR / "secret_words.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(selected, f, ensure_ascii=False, indent=2)

    by_cat: dict[str, int] = {}
    for c in selected:
        by_cat[c["category"]] = by_cat.get(c["category"], 0) + 1

    print(f"Selected {len(selected)} curated secret words (of {len(seen)} unique candidates).")
    print("By category:", by_cat)
    if missing:
        print(f"Not found in vocab (skipped, {len(missing)}): {missing}")
    print(f"Wrote {out_path}")


if __name__ == "__main__":
    main()
