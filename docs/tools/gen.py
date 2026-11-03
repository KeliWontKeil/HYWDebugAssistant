"""文档站构建钩子：注入版本信息、生成参考页内容。

只做构建期生成，不写回仓库文件：
- `{{version}}` / `{{commit}}` 占位符替换；
- `<!-- gen:modules -->` 由模块清单生成；
- `<!-- gen:changelog -->` / `<!-- gen:recent -->` 由 git 提交记录生成。
"""

from __future__ import annotations

import datetime
import json
import re
import subprocess
from pathlib import Path
from xml.etree import ElementTree

ROOT = Path(__file__).resolve().parents[2]
VERSION_FILE = ROOT / "version.props"
MODULE_ROOT = ROOT / "src" / "Modules" / "Business"

_VERSION_SUBJECT = re.compile(r"^(\d+\.\d+\.\d+\.\d+)\s+(.*)$")
_UNNUMBERED = "未编号提交"


def _version() -> str:
    try:
        root = ElementTree.parse(VERSION_FILE).getroot()
        group = root.find("PropertyGroup")
        if group is None:
            return "0.0.0.0"
        names = ("HywVersionMajor", "HywVersionMinor", "HywVersionPatch", "HywVersionRound")
        return ".".join((group.findtext(name) or "0").strip() for name in names)
    except (OSError, ElementTree.ParseError):
        return "0.0.0.0"


def _git(*args: str) -> str:
    result = subprocess.run(
        ["git", *args],
        cwd=ROOT,
        capture_output=True,
        text=True,
        encoding="utf-8",
        check=True,
    )
    return result.stdout


def _commit() -> str:
    try:
        return _git("rev-parse", "--short", "HEAD").strip() or "unknown"
    except (OSError, subprocess.CalledProcessError):
        return "unknown"


def _entries() -> list[dict[str, str]]:
    try:
        raw = _git("log", "--date=short", "--pretty=format:%h%x09%ad%x09%s")
    except (OSError, subprocess.CalledProcessError):
        return []

    entries: list[dict[str, str]] = []
    for line in raw.splitlines():
        parts = line.split("\t", 2)
        if len(parts) != 3:
            continue
        sha, date, subject = parts
        match = _VERSION_SUBJECT.match(subject)
        entries.append(
            {
                "version": match.group(1) if match else "",
                "text": (match.group(2) if match else subject).strip(),
                "date": date,
                "sha": sha,
            }
        )
    return entries


def _changelog(limit: int | None = None) -> str:
    grouped: dict[str, list[dict[str, str]]] = {}
    for entry in _entries():
        grouped.setdefault(entry["version"] or _UNNUMBERED, []).append(entry)

    versions = sorted(
        (version for version in grouped if version != _UNNUMBERED),
        key=lambda version: tuple(int(part) for part in version.split(".")),
        reverse=True,
    )
    if _UNNUMBERED in grouped:
        versions.append(_UNNUMBERED)

    lines: list[str] = []
    shown = 0
    for version in versions:
        if limit is not None and shown >= limit:
            break
        lines.extend((f"## {version}", ""))
        for entry in grouped[version]:
            lines.append(f"- {entry['date']} · `{entry['sha']}` · {entry['text']}")
            shown += 1
            if limit is not None and shown >= limit:
                break
        lines.append("")
    return "\n".join(lines).rstrip() + "\n"


def _module_rows() -> list[dict[str, str]]:
    rows: list[dict[str, str]] = []
    for manifest in sorted(MODULE_ROOT.glob("*/module.json")):
        data = json.loads(manifest.read_text(encoding="utf-8-sig"))
        short_name = Path(data.get("entryAssembly", "")).stem
        entries: dict[str, str] = {}
        for name in ("zh-Hans.json", "en.json"):
            table = manifest.parent / "resources" / "i18n" / short_name / name
            if table.exists():
                entries = json.loads(table.read_text(encoding="utf-8-sig")).get("entries", {})
                break
        rows.append(
            {
                "title": entries.get("module.title") or short_name,
                "id": str(data.get("id", "")),
                "description": entries.get("module.description") or "",
            }
        )
    return sorted(rows, key=lambda row: row["id"])


def _modules() -> str:
    rows = _module_rows()
    if not rows:
        return "> 未找到模块清单。\n"

    lines = ["| 模块 | 标识 | 说明 |", "|---|---|---|"]
    for row in rows:
        description = row["description"].replace("|", "\\|")
        lines.append(f"| {row['title']} | `{row['id']}` | {description} |")
    return "\n".join(lines) + "\n"


def on_config(config, **kwargs):
    config.extra["version"] = _version()
    config.extra["commit"] = _commit()
    config.extra["build_date"] = datetime.date.today().isoformat()
    return config


def on_page_markdown(markdown, page, config, files):
    version = config.extra["version"]
    commit = config.extra["commit"]
    text = markdown.replace("{{version}}", version).replace("{{commit}}", commit)

    if "<!-- gen:modules -->" in text:
        text = text.replace("<!-- gen:modules -->", _modules())
    if "<!-- gen:changelog -->" in text:
        text = text.replace("<!-- gen:changelog -->", _changelog())
    if "<!-- gen:recent -->" in text:
        text = text.replace("<!-- gen:recent -->", _changelog(limit=5))
    return text
