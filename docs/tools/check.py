"""文档结构检查：导航完整、目录归属、语言对照、构建产物自检。

只检查结构性事实（页面是否存在、是否登记、目录是否在允许范围内），
不检查措辞、行数或内容质量。
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

import yaml

ALLOWED_DIRS = ("start", "guides", "concepts", "develop", "reference", "about")
VERSION_RE = re.compile(r"\d+\.\d+\.\d+\.\d+")


def load_config(path: Path) -> dict:
    data = yaml.safe_load(path.read_text(encoding="utf-8")) or {}
    inherit = data.pop("INHERIT", None)
    merged: dict = {}
    if inherit:
        merged.update(load_config((path.parent / inherit).resolve()))
    merged.update(data)
    return merged


def collect_nav(items, acc: list[str]) -> None:
    if items is None:
        return
    if isinstance(items, str):
        acc.append(items.replace("\\", "/"))
        return
    for item in items:
        if isinstance(item, str):
            acc.append(item.replace("\\", "/"))
        elif isinstance(item, dict):
            for value in item.values():
                collect_nav(value, acc)


def resolve_docs_dir(config_path: Path, config: dict) -> Path:
    """docs_dir 相对配置文件所在目录解析，不依赖进程当前目录。"""
    candidate = Path(config["docs_dir"])
    return (candidate if candidate.is_absolute() else config_path.parent / candidate).resolve()


def check_structure(config_path: Path, errors: list[str]) -> tuple[Path, set[str], list[str]]:
    config = load_config(config_path)
    docs_dir = resolve_docs_dir(config_path, config)

    nav_pages: list[str] = []
    collect_nav(config.get("nav"), nav_pages)

    missing = [page for page in nav_pages if not (docs_dir / page).is_file()]
    for page in missing:
        errors.append(f"nav 指向不存在的页面：{page}")

    on_disk = {p.relative_to(docs_dir).as_posix() for p in docs_dir.rglob("*.md")}
    for page in sorted(on_disk - set(nav_pages)):
        errors.append(f"页面没有登记到 nav：{page}")

    for directory in sorted(p for p in docs_dir.iterdir() if p.is_dir()):
        if directory.name not in ALLOWED_DIRS:
            errors.append(f"docs 目录下出现未登记的目录：{directory.name}")
        if not (directory / "index.md").is_file():
            errors.append(f"目录缺少首页 index.md：{directory.name}")

    return docs_dir, on_disk, nav_pages


def check_parity(source_pages: set[str], other_config: Path, errors: list[str]) -> None:
    other_dir = resolve_docs_dir(other_config, load_config(other_config))
    if not other_dir.is_dir():
        errors.append(f"对照语言目录不存在：{other_dir}")
        return
    other_pages = {p.relative_to(other_dir).as_posix() for p in other_dir.rglob("*.md")}
    for page in sorted(source_pages - other_pages):
        print(f"[info] 尚未翻译：{page}")
    for page in sorted(other_pages - source_pages):
        errors.append(f"对应语言页没有匹配的中文源页：{page}")


def check_site(site_dir: Path, lang: str, errors: list[str]) -> None:
    index = site_dir / "index.html"
    if not index.is_file():
        errors.append(f"构建产物缺少 index.html：{site_dir}")
        return
    search = site_dir / "search" / "search_index.json"
    if not search.is_file():
        errors.append(f"构建产物缺少搜索索引：{search}")
    else:
        data = json.loads(search.read_text(encoding="utf-8"))
        if not data.get("docs"):
            errors.append("搜索索引为空")

    html = index.read_text(encoding="utf-8")
    if not VERSION_RE.search(html):
        errors.append(f"首页没有注入版本号（{lang}）")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--config", required=True)
    parser.add_argument("--site", default=None)
    parser.add_argument("--lang", default="zh")
    parser.add_argument("--parity-config", default=None)
    args = parser.parse_args()

    errors: list[str] = []
    docs_dir, pages, nav = check_structure(Path(args.config), errors)

    if args.site:
        check_site(Path(args.site), args.lang, errors)

    if args.parity_config:
        check_parity(pages, Path(args.parity_config), errors)

    for error in errors:
        print(f"[FAIL] {error}")
    if errors:
        return 1

    print(f"[OK] 结构检查通过：{len(pages)} 页，{len(nav)} 个导航项")
    return 0


if __name__ == "__main__":
    sys.exit(main())
