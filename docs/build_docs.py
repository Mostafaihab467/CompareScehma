"""Regenerate the agent-facing docs set under docs/.

Every source file gets a pointer page in docs/files/<same/relative/path>.md and
docs/INDEX.md maps all of them. Run from the repository root:

    python docs/build_docs.py

Purpose lines come from PURPOSES below (or a file's own XML-doc/summary comment),
so keep a page's "Purpose" honest by editing the dict here and re-running: the
rest of each page is extracted from the source, never copied by hand.
"""

import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parent.parent
DOCS = ROOT / "docs"
OUT = DOCS / "files"

SOURCE_GLOBS = ["*.cs", "App.axaml", "App.axaml.cs", "Views/*", "Controls/*",
                "Models/*", "ViewModels/*", "Services/*", "Styles/*",
                "Resources/*", "Scripts/*", "Tools/*"]

DOC_RE = re.compile(r"///\s*<summary>(.*?)</summary>", re.S | re.M)
LINE_DOC_RE = re.compile(r"^\s*///\s*(.*)$")
TYPE_RE = re.compile(
    r"^(?P<indent>[ \t]*)(?P<mods>(?:public|internal|sealed|static|partial|abstract|readonly|file|\s)+)?"
    r"(?P<kind>class|record|struct|enum|interface)\s+(?P<name>\w+)"
    r"(?P<rest>[^;{]*)", re.M)
NS_RE = re.compile(r"^namespace\s+([\w.]+)\s*;", re.M)
MEMBER_RE = re.compile(
    r"^\s*public\s+(?:(?:static|readonly|virtual|override|async|partial|sealed|\[.*?\])\s+)*"
    r"[\w<>,\[\]\.\?\s]+?\s+(?P<name>\w+)\s*(?P<open>[(\{=])", re.M)
AXAML_CLASS_RE = re.compile(r'x:Class="([\w.]+)"')
AXAML_NAME_RE = re.compile(r'x:Name="(\w+)"')


def sources():
    seen, out = set(), []
    for pattern in SOURCE_GLOBS:
        for p in sorted(ROOT.glob(pattern)):
            if p.is_file() and p not in seen:
                seen.add(p)
                out.append(p)
    return out


def first_sentence(text):
    text = re.sub(r"<c>|</c>|<see\s+[^>]*/>|<see\s+[^>]*>|</see>|</?summary>", "", text)
    text = re.sub(r"\s+", " ", text).strip()
    if not text:
        return ""
    m = re.search(r"(?<=[.!?])\s", text)
    return text[: m.start()].strip() if m else text


def summaries(text):
    """Every doc comment in the file, in order, with the line it sits above."""
    found = []
    lines = text.splitlines()
    i = 0
    while i < len(lines):
        if lines[i].strip().startswith("///"):
            block = []
            while i < len(lines) and lines[i].strip().startswith("///"):
                block.append(LINE_DOC_RE.match(lines[i]).group(1))
                i += 1
            while i < len(lines) and not lines[i].strip():
                i += 1
            if i < len(lines):
                found.append((lines[i].strip(), " ".join(b.strip() for b in block)))
        i += 1
    return found


def declared_types(text):
    return [(m.group("kind"), m.group("name"), m.group("rest").strip())
            for m in TYPE_RE.finditer(text)]


def public_members(text):
    out, seen = [], set()
    for m in MEMBER_RE.finditer(text):
        name = m.group("name")
        if name in seen or m.group("open") == "=":
            continue
        seen.add(name)
        out.append(name + ("()" if m.group("open") == "(" else ""))
    return out


def lines_of(text):
    return text.count("\n") + (0 if text.endswith("\n") else 1)


def referenced_by(type_names, path, all_sources):
    hits = set()
    pattern = re.compile(r"\b(" + "|".join(re.escape(n) for n in type_names) + r")\b")
    for other in all_sources:
        rel = other.relative_to(ROOT).as_posix()
        if rel == path:
            continue
        try:
            body = other.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:
            continue
        if pattern.search(body):
            hits.add(rel)
    return sorted(hits)


GROUP_ORDER = ["(root)", "Views", "Controls", "ViewModels", "Models", "Services",
               "Styles", "Resources", "Scripts", "Tools"]


def group_of(path):
    return path.split("/")[0] if "/" in path else "(root)"


def purpose(path, text, purposes):
    explicit = purposes.get(path)
    if explicit:
        return explicit
    for _, block in summaries(text):
        line = first_sentence(block)
        if line:
            return f"{line} _(taken from the file's own doc comment)_"
    return ""


def page(path, text, all_sources, purposes):
    types = declared_types(text)
    names = [t[1] for t in types] or [pathlib.Path(path).stem]
    ns = NS_RE.search(text)
    axaml = path.endswith(".axaml")
    parts = [f"# `{path}`", ""]
    parts += [f"**Purpose:** {purpose(path, text, purposes)}", ""]
    if ns:
        parts += [f"**Namespace:** `{ns.group(1)}`", ""]
    elif axaml:
        cls = AXAML_CLASS_RE.search(text)
        if cls:
            parts += [f"**Maps to:** `{cls.group(1)}` (code-behind "
                      f"`{path}.cs`)", ""]
    if path.endswith(".axaml.cs"):
        parts += [f"**Code-behind for:** `{path[:-3]}`", ""]
    if types:
        docmap = dict(summaries(text))
        parts += ["## Declared types", ""]
        for kind, name, rest in types:
            sig = f"`{kind} {name}`"
            if rest:
                sig += f" {rest}".replace("\n", " ")
            doc = first_sentence(docmap.get(next(
                (line for line in text.splitlines()
                 if re.search(rf"\b{kind}\s+.*\b{name}\b", line)), ""), ""))
            parts += [f"- {sig}" + (f" — {doc}" if doc else "")]
        parts += [""]
    members = public_members(text)
    if members:
        parts += ["## Public surface", "",
                  ", ".join(f"`{m}`" for m in members[:60])]
        if len(members) > 60:
            parts[-1] += f" (+{len(members) - 60} more)"
        parts += [""]
    if axaml and not path.endswith(".cs"):
        named = AXAML_NAME_RE.findall(text)
        if named:
            parts += ["## Named controls", "",
                      ", ".join(f"`{n}`" for n in named[:80]), ""]
    refs = referenced_by(names, path, all_sources)
    parts += ["## Referenced by", ""]
    parts += [f"- `{r}`" for r in refs[:25]] if refs else ["- nothing else in the repo"]
    if len(refs) > 25:
        parts += [f"- (+{len(refs) - 25} more)"]
    parts += ["", f"**Size:** {lines_of(text)} lines", ""]
    return "\n".join(parts)


def main():
    files = sources()
    texts = {}
    for p in files:
        try:
            texts[p.relative_to(ROOT).as_posix()] = p.read_text(encoding="utf-8-sig")
        except UnicodeDecodeError:
            texts[p.relative_to(ROOT).as_posix()] = p.read_text(
                encoding="utf-8", errors="replace")
    purposes = load_purposes()
    OUT.mkdir(parents=True, exist_ok=True)
    index = ["# Source index",
             "",
             "One page per source file: purpose, declared types, public surface and "
             "who references it. Read a page instead of opening the file whenever you "
             "only need to know where something lives. Module map and how-to-find-it "
             "table: [README](README.md). Feature status: [../PRODUCTION.md](../PRODUCTION.md).",
             ""]
    ordered = sorted(texts, key=lambda p: (GROUP_ORDER.index(group_of(p))
                                           if group_of(p) in GROUP_ORDER else 99, p))
    group = None
    for path in ordered:
        body = page(path, texts[path], files, purposes)
        target = OUT / (path + ".md")
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(body, encoding="utf-8")
        head = group_of(path)
        if head != group:
            group = head
            index += ([""] if index and index[-1] != "" else []) + [f"## {head}", ""]
        note = "" if path in purposes else " _(purpose not written yet — add it to docs/purposes.py)_"
        index += [f"- [`{path}`](files/{path}.md) — {purpose(path, texts[path], purposes)}{note}"]
    index += ["", f"_{len(texts)} pages. Regenerate after any code change with "
                  "`python docs/build_docs.py`._"]
    (DOCS / "INDEX.md").write_text("\n".join(index) + "\n", encoding="utf-8")
    missing = [p for p in texts if p not in purposes]
    print(f"wrote {len(texts)} pages; {len(missing)} without a purpose line")
    for m in sorted(missing):
        print("  no purpose:", m)


PURPOSES_FILE = DOCS / "purposes.py"


def load_purposes():
    if not PURPOSES_FILE.exists():
        return {}
    ns = {}
    exec(compile(PURPOSES_FILE.read_text(encoding="utf-8"), str(PURPOSES_FILE), "exec"), ns)
    return ns.get("PURPOSES", {})


if __name__ == "__main__":
    main()
