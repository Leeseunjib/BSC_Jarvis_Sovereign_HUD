import os
import json
from datetime import datetime

MEMO_DIR = r"C:\자비스\data\memos"

def init_memory():
    if not os.path.exists(MEMO_DIR):
        os.makedirs(MEMO_DIR, exist_ok=True)

def save_memory(title, content):
    init_memory()
    filename = f"{datetime.now().strftime('%Y%m%d_%H%M%S')}_{title}.json"
    filepath = os.path.join(MEMO_DIR, filename)
    data = {
        "timestamp": datetime.now().isoformat(),
        "title": title,
        "content": content
    }
    with open(filepath, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=4)
    return filepath

def load_recent_memories(limit=5):
    init_memory()
    if not os.path.exists(MEMO_DIR):
        return []
    files = sorted([os.path.join(MEMO_DIR, f) for f in os.listdir(MEMO_DIR) if f.endswith('.json')], key=os.path.getmtime, reverse=True)
    memories = []
    for file in files[:limit]:
        try:
            with open(file, "r", encoding="utf-8") as f:
                memories.append(json.load(f))
        except Exception:
            pass
    return memories
