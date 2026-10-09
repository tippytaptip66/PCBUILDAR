"""Builds each lesson's 20-question quiz from lesson_quizzes.json.

    python Tools/lesson-quizzes/build_lesson_quizzes.py           (writes the assets)
    python Tools/lesson-quizzes/build_lesson_quizzes.py --check   (validates only, writes nothing)

For every question it writes a QuizQuestionSO asset to Assets/_Project/Data/LessonQuizzes/<lesson>/<id>.asset and lists
them, in order, in the lesson's `quiz` field. {"ref": name} entries reuse an existing card from
Assets/_Project/Data/QuizQuestions instead of writing a new one.

Asset GUIDs are derived from the question id, so re-running updates the same assets in place and nothing that points at
them breaks. Questions removed from the JSON have their assets deleted. Unity picks the changes up when it next gets focus.
"""
import json, pathlib, re, sys, uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
DATA = ROOT / "Assets" / "_Project" / "Data"
SOURCE = pathlib.Path(__file__).with_name("lesson_quizzes.json")
OUT = DATA / "LessonQuizzes"
LESSONS = DATA / "Resources" / "Lessons"
CARDS = DATA / "QuizQuestions"
QUESTIONS_PER_LESSON = 20
SCRIPT_META = ROOT / "Assets" / "_Project" / "Scripts" / "Data" / "QuizQuestionSO.cs.meta"
TYPES = {"mc": 0, "tf": 1, "match": 3, "order": 4}   # QuizQuestionSO.QuestionType (2 = Typing, not used here)


def guid_for(key):
    return uuid.uuid5(uuid.NAMESPACE_URL, "buildar/lesson-quiz/" + key).hex


def meta_guid(path):
    m = re.search(r"^guid: (\w+)", path.read_text(encoding="utf-8"), re.M)
    if not m: raise SystemExit(f"no guid in {path}")
    return m.group(1)


def q(s):
    """A YAML double-quoted scalar Unity reads back exactly (non-ASCII as \\u escapes)."""
    return json.dumps(s, ensure_ascii=True)


def validate(data):
    errors, ids, texts = [], set(), set()
    lesson_ids = {p.stem for p in LESSONS.glob("*.asset")}
    for lesson in data["lessons"]:
        lid = lesson["lesson"]
        if lid not in lesson_ids: errors.append(f"{lid}: no lesson asset with that name")
        items = lesson["questions"]
        if len(items) != QUESTIONS_PER_LESSON:
            errors.append(f"{lid}: {len(items)} questions, needs {QUESTIONS_PER_LESSON}")
        for i, item in enumerate(items, 1):
            where = f"{lid} #{i}"
            if "ref" in item:
                if not (CARDS / f"{item['ref']}.asset").exists(): errors.append(f"{where}: no card '{item['ref']}'")
                key = item["ref"]
            else:
                key = item.get("id", "")
                t = item.get("type")
                if not key: errors.append(f"{where}: missing id")
                if t not in TYPES: errors.append(f"{where}: unknown type {t!r}")
                if not item.get("q", "").strip(): errors.append(f"{where}: empty question")
                if not item.get("why", "").strip(): errors.append(f"{where}: no explanation")
                text = item.get("q", "").strip().lower()
                if t != "match" and text in texts: errors.append(f"{where}: duplicate question text")
                texts.add(text)
                if t == "mc":
                    opts = item.get("options", [])
                    if len(opts) != 4 or len(set(opts)) != 4: errors.append(f"{where}: needs 4 different options")
                    if not (0 <= item.get("answer", -1) < len(opts)): errors.append(f"{where}: answer index out of range")
                elif t == "tf" and not isinstance(item.get("answer"), bool): errors.append(f"{where}: answer must be true/false")
                elif t == "match":
                    pairs = item.get("pairs", [])
                    if len(pairs) < 2 or len({r for _, r in pairs}) != len(pairs) or len({l for l, _ in pairs}) != len(pairs):
                        errors.append(f"{where}: needs 2+ pairs with different left and right sides")
                elif t == "order":
                    order = item.get("order", [])
                    if len(order) < 3 or len(set(order)) != len(order): errors.append(f"{where}: needs 3+ different items")
            if key in ids: errors.append(f"{where}: id '{key}' used twice")
            ids.add(key)
    return errors


def card_asset(item, script_guid):
    t = TYPES[item["type"]]
    options = item.get("options") if item["type"] == "mc" else item.get("order") if item["type"] == "order" else []
    lines = [
        "%YAML 1.1", "%TAG !u! tag:unity3d.com,2011:", "--- !u!114 &11400000", "MonoBehaviour:",
        "  m_ObjectHideFlags: 0", "  m_CorrespondingSourceObject: {fileID: 0}", "  m_PrefabInstance: {fileID: 0}",
        "  m_PrefabAsset: {fileID: 0}", "  m_GameObject: {fileID: 0}", "  m_Enabled: 1", "  m_EditorHideFlags: 0",
        f"  m_Script: {{fileID: 11500000, guid: {script_guid}, type: 3}}",
        f"  m_Name: {item['id']}",
        "  m_EditorClassIdentifier: Assembly-CSharp::BuildAR.Data.QuizQuestionSO",
        f"  questionType: {t}",
        f"  questionText: {q(item['q'])}",
    ]
    lines += ["  options:"] + [f"  - {q(o)}" for o in options] if options else ["  options: []"]
    lines += [
        f"  correctOptionIndex: {item.get('answer', 0) if item['type'] == 'mc' else 0}",
        f"  statementIsTrue: {1 if item['type'] == 'tf' and item['answer'] else 0}",
        "  answer: ", "  acceptedAnswers: []", "  distractors: []",
    ]
    pairs = item.get("pairs", []) if item["type"] == "match" else []
    lines += ["  pairs:"] + [f"  - left: {q(l)}\n    right: {q(r)}" for l, r in pairs] if pairs else ["  pairs: []"]
    lines += [f"  explanation: {q(item['why'])}", f"  isSafetyQuestion: {1 if item.get('safety') else 0}"]
    return "\n".join(lines) + "\n"


def asset_meta(guid):
    return (f"fileFormatVersion: 2\nguid: {guid}\nNativeFormatImporter:\n  externalObjects: {{}}\n"
            f"  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def folder_meta(guid):
    return (f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n"
            f"  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def write_if_changed(path, text):
    if path.exists() and path.read_text(encoding="utf-8") == text: return False
    path.write_text(text, encoding="utf-8", newline="\n")
    return True


def main():
    data = json.loads(SOURCE.read_text(encoding="utf-8"))
    errors = validate(data)
    if errors:
        print("Not built, fix these first:")
        for e in errors: print("  -", e)
        sys.exit(1)
    total = sum(len(l["questions"]) for l in data["lessons"])
    reused = sum(1 for l in data["lessons"] for i in l["questions"] if "ref" in i)
    print(f"OK: {len(data['lessons'])} lessons, {total} questions ({total - reused} new, {reused} reused cards).")
    if "--check" in sys.argv: return

    script_guid = meta_guid(SCRIPT_META)
    OUT.mkdir(exist_ok=True)
    write_if_changed(OUT.with_name(OUT.name + ".meta"), folder_meta(guid_for("folder")))
    changed = 0
    for lesson in data["lessons"]:
        lid = lesson["lesson"]
        folder = OUT / lid
        folder.mkdir(exist_ok=True)
        write_if_changed(folder.with_name(folder.name + ".meta"), folder_meta(guid_for("folder/" + lid)))

        refs, keep = [], set()
        for item in lesson["questions"]:
            if "ref" in item:
                refs.append(meta_guid(CARDS / f"{item['ref']}.asset.meta"))
                continue
            guid = guid_for(item["id"])
            path = folder / f"{item['id']}.asset"
            changed += write_if_changed(path, card_asset(item, script_guid))
            write_if_changed(path.with_name(path.name + ".meta"), asset_meta(guid))
            keep.add(path.name)
            refs.append(guid)

        for stale in folder.glob("*.asset"):   # questions taken out of the JSON
            if stale.name not in keep:
                stale.unlink()
                stale.with_name(stale.name + ".meta").unlink(missing_ok=True)
                print(f"  removed {lid}/{stale.name}")

        lesson_path = LESSONS / f"{lid}.asset"
        text = lesson_path.read_text(encoding="utf-8")
        block = "  quiz:\n" + "".join(f"  - {{fileID: 11400000, guid: {g}, type: 2}}\n" for g in refs)
        new = re.sub(r"^  quiz:.*\n(?:  - \{.*\}\n)*", block, text, count=1, flags=re.M)
        if new == text and block not in text: raise SystemExit(f"{lid}: couldn't find the quiz field")
        changed += write_if_changed(lesson_path, new)
    print(f"Wrote {changed} changed file(s). Switch to Unity to import them.")


if __name__ == "__main__":
    main()
