# Fetches the Hyprland wiki into markdown. dev.ps1 calls this.
# Do not edit the pages it writes.

import argparse
import os
import re
import sys
import threading
import urllib.error
import urllib.parse
import urllib.request
from concurrent.futures import ThreadPoolExecutor, as_completed
from datetime import date
from pathlib import Path

from bs4 import BeautifulSoup
from markdownify import markdownify

USER_AGENT = "winland-docs-fetch"
TODAY = date.today().isoformat()

ENTRY = "https://wiki.hypr.land/"
SITEMAP = "https://wiki.hypr.land/sitemap.xml"
PREFIX = "https://wiki.hypr.land/"
HOSTS = {"wiki.hypr.land"}
BLURB = (
    "A copy of the Hyprland wiki, kept here so Winland can be checked against it. "
    "The wiki tracks the latest Hyprland git commit, not a tagged release."
)
OWNERS = "The text belongs to the Hyprland authors."
REFRESH = "pwsh dev.ps1 -Refresh"
SKIP = {"https://wiki.hypr.land/tags", "https://wiki.hypr.land/categories"}


def fetch(url):
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    last = None
    for _ in range(3):
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                return response.read()
        except (urllib.error.URLError, TimeoutError) as error:
            last = error
    raise last


def add_url(urls, seen, url):
    bare = url.split("#", 1)[0].rstrip("/")
    if bare in seen:
        return
    seen.add(bare)
    # A trailing slash on a file is a 404.
    if Path(urllib.parse.urlparse(bare).path).suffix:
        urls.append(bare)
    else:
        urls.append(bare + "/")


def sitemap_urls():
    urls = []
    seen = set()
    raw = fetch(SITEMAP).decode("utf-8", errors="replace")
    for url in re.findall(r"<loc>\s*([^<\s]+)\s*</loc>", raw):
        url = url.split("#", 1)[0]
        if not url.startswith(PREFIX):
            continue
        if url.rstrip("/") in SKIP:
            continue
        add_url(urls, seen, url)
    add_url(urls, seen, ENTRY)
    return urls


def page_key(url):
    path = urllib.parse.urlparse(url).path
    prefix = urllib.parse.urlparse(PREFIX).path
    if path.startswith(prefix):
        path = path[len(prefix):]
    return path.strip("/")


def file_for(key, keys):
    if key == "":
        return "index.md"
    if any(other.startswith(key + "/") for other in keys):
        return key + "/index.md"
    return key + ".md"


def clean_title(text):
    text = re.sub(r"\s+", " ", text or "").strip()
    for suffix in (" – Hyprland Wiki", " - Hyprland Wiki"):
        if text.endswith(suffix):
            text = text[: -len(suffix)].strip()
    return text or "Untitled"


def prepare(html, page_url, image_dir, page_file):
    soup = BeautifulSoup(html, "html.parser")
    nodes = soup.select("div.content")
    node = next((item for item in nodes if item.find(["h1", "h2"])), None)
    if node is None and nodes:
        node = nodes[-1]
    if node is None:
        node = soup.select_one("main") or soup.select_one("article") or soup.body
    title_node = soup.find("title")
    title = clean_title(title_node.get_text(" ", strip=True) if title_node else "")

    for tag in node.select("script, style, svg, button, nav, form, astro-island"):
        tag.decompose()

    # Hextra callouts: a bordered box whose first line is Note, Warning, and so on.
    callouts = []
    for box in node.select("div"):
        classes = box.get("class") or []
        if "hx:rounded-lg" not in classes or "hx:border" not in classes:
            continue
        title_line = box.find("p")
        label = title_line.get_text(" ", strip=True) if title_line is not None else ""
        kind = {
            "Note": "NOTE",
            "Info": "NOTE",
            "Tip": "TIP",
            "Important": "IMPORTANT",
            "Warning": "WARNING",
            "Caution": "CAUTION",
            "Danger": "CAUTION",
        }.get(label)
        if kind is None:
            continue
        callouts.append((box, title_line, kind))
    for box, title_line, kind in reversed(callouts):
        quote = soup.new_tag("blockquote")
        marker = soup.new_tag("p")
        marker.string = f"[!{kind}]"
        quote.append(marker)
        for child in list(box.contents):
            if child is title_line:
                continue
            quote.append(child.extract())
        box.replace_with(quote)

    for alert in node.select(".markdown-alert"):
        kind = "NOTE"
        for name in alert.get("class") or []:
            if name.startswith("markdown-alert-"):
                kind = name[len("markdown-alert-"):].upper()
        label = alert.select_one(".markdown-alert-title")
        if label is not None:
            label.decompose()
        quote = soup.new_tag("blockquote")
        marker = soup.new_tag("p")
        marker.string = f"[!{kind}]"
        quote.append(marker)
        for child in list(alert.contents):
            quote.append(child.extract())
        alert.replace_with(quote)

    for pre in list(node.select("pre")):
        button = pre.select_one("[data-code]")
        if button is None or not button.get("data-code"):
            continue
        language = ""
        code = pre.find("code")
        if code is not None:
            for name in code.get("class") or []:
                if name.startswith("language-"):
                    language = name
        replacement = soup.new_tag("code")
        if language:
            replacement["class"] = [language]
        replacement.string = button["data-code"]
        fence = soup.new_tag("pre")
        fence.append(replacement)
        pre.replace_with(fence)

    for video in list(node.select("video")):
        source = video.find("source")
        src = (source.get("src") if source else None) or video.get("src") or ""
        link = soup.new_tag("a", href=urllib.parse.urljoin(page_url, src) if src else page_url)
        link.string = "Video"
        paragraph = soup.new_tag("p")
        paragraph.append(link)
        video.replace_with(paragraph)

    for image in node.select("img"):
        src = image.get("src")
        if not src or src.startswith("data:"):
            continue
        absolute = urllib.parse.urljoin(page_url, src)
        host = urllib.parse.urlparse(absolute).netloc
        if host not in HOSTS:
            image["src"] = absolute
            continue
        suffix = Path(urllib.parse.urlparse(absolute).path).suffix.lower()
        if suffix not in {".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif"}:
            suffix = ".img"
        name = re.sub(r"[^a-z0-9.-]+", "-", urllib.parse.urlparse(absolute).path.strip("/").lower())
        if not name.endswith(suffix):
            name += suffix
        target = image_dir / name
        with IMAGE_LOCK:
            if not target.exists():
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(fetch(absolute))
        image["src"] = link_to((Path("images") / name).as_posix(), page_file)

    return title, node


def link_to(target, source_file):
    start = Path(source_file).parent
    start_text = "." if str(start) == "." else start.as_posix()
    return os.path.relpath(target, start_text).replace("\\", "/")


IMAGE_LOCK = threading.Lock()


def fence_language(pre):
    nodes = [pre]
    code = pre.find("code")
    if code is not None:
        nodes.append(code)
    for node in nodes:
        for name in node.get("class") or []:
            if name.startswith("language-") and name != "language-fallback":
                return name[len("language-"):]
        if node.get("data-lang"):
            return node["data-lang"]
    return ""


def to_markdown(node):
    text = markdownify(str(node), heading_style="ATX", bullets="-", code_language_callback=fence_language)
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    text = re.sub(r"\n{3,}", "\n\n", text).strip()
    text = re.sub(r"^(?:---\n+)+", "", text)
    return text.strip() + "\n"


def rewrite_links(text, page_url, page_file, files_by_key):
    def replace(match):
        href = match.group(1)
        if href.startswith("#"):
            return match.group(0)
        absolute = urllib.parse.urljoin(page_url, href)
        parsed = urllib.parse.urlparse(absolute)
        anchor = f"#{parsed.fragment}" if parsed.fragment else ""
        key_url = parsed._replace(fragment="", query="").geturl().rstrip("/")
        prefix = PREFIX.rstrip("/")
        if parsed.netloc in HOSTS:
            key = page_key(key_url + "/")
            target = files_by_key.get(key)
            if target is not None:
                link = link_to(target, page_file)
                return f"]({link}{anchor})"
        if not key_url.startswith(prefix):
            if href.startswith("/") and parsed.netloc in HOSTS:
                return f"]({parsed._replace(query='').geturl()})"
            return match.group(0)
        key = page_key(key_url + "/")
        target = files_by_key.get(key)
        if target is not None:
            link = link_to(target, page_file)
            return f"]({link}{anchor})"
        # A root-relative link to a page this copy does not have. Keep it absolute
        # so it still opens on the original site.
        if href.startswith("/"):
            return f"]({parsed._replace(query='').geturl()})"
        return match.group(0)

    return re.sub(r"\]\(([^)\s]+)\)", replace, text)


def sidebar(html):
    soup = BeautifulSoup(html, "html.parser")
    items = []
    seen = set()
    for anchor in soup.select("aside a[href]"):
        href = anchor.get("href") or ""
        if href.startswith("#"):
            continue
        absolute = urllib.parse.urljoin(ENTRY, href).split("#", 1)[0].rstrip("/")
        if not absolute.startswith(PREFIX.rstrip("/")):
            continue
        key = page_key(absolute + "/")
        if key in seen:
            continue
        title = re.sub(r"\s+", " ", anchor.get_text(" ", strip=True))
        if not title:
            continue
        seen.add(key)
        items.append((key, title))
    return items


def write_readme(root, pages, order):
    lines = [
        "# Hyprland documentation",
        "",
        f"{BLURB} Fetched {TODAY}.",
        "",
        f"{OWNERS} Do not edit these files. Run `{REFRESH}` to refresh them.",
        "",
        f"From <{ENTRY}>.",
        "",
        "Videos stay linked to `dl.hypr.land`. They are not copied into this folder.",
        "",
        "## Pages",
        "",
    ]
    for key, title in order:
        target = pages.get(key)
        if target is None:
            continue
        depth = 0 if key == "" else key.count("/")
        indent = "  " * depth
        lines.append(f"{indent}- [{title}]({target.replace(chr(92), '/')})")
    lines.append("")
    (root / "README.md").write_text("\n".join(lines), encoding="utf-8")


def clear_previous(root):
    if not root.exists():
        root.mkdir(parents=True)
        return
    for path in root.rglob("*"):
        if path.is_file() and path.suffix.lower() in {".md", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".avif", ".img"}:
            path.unlink()
    for path in sorted(root.rglob("*"), reverse=True):
        if path.is_dir() and not any(path.iterdir()):
            path.rmdir()


def one(url, files_by_key, image_dir):
    key = page_key(url)
    target = files_by_key[key]
    html = fetch(url)
    title, node = prepare(html, url, image_dir, target)
    body = to_markdown(node)
    if not body.lstrip().startswith("#"):
        body = f"# {title}\n\n{body}"
    body = rewrite_links(body, url, target, files_by_key)
    header = f"<!-- Source: {url} · fetched {TODAY}. Do not edit, run pwsh dev.ps1. -->\n\n"
    return key, title, target, header + body


def main():
    parser = argparse.ArgumentParser(description="Fetch the Hyprland wiki into markdown.")
    parser.add_argument("--root", required=True)
    parser.add_argument("--only", action="append", default=[])
    args = parser.parse_args()
    root = Path(args.root)
    all_urls = sitemap_urls()
    urls = all_urls
    if args.only:
        wanted = {item.rstrip("/") for item in args.only}
        urls = [url for url in all_urls if url.rstrip("/") in wanted]
        if not urls:
            sys.exit("none of --only matched the sitemap")
    key_set = {page_key(url) for url in all_urls}
    files_by_key = {key: file_for(key, key_set) for key in key_set}
    if not args.only:
        clear_previous(root)
    image_dir = root / "images"
    image_dir.mkdir(parents=True, exist_ok=True)

    pages = {}
    titles = {}
    failures = []
    done = 0
    with ThreadPoolExecutor(max_workers=6) as pool:
        futures = {pool.submit(one, url, files_by_key, image_dir): url for url in urls}
        for future in as_completed(futures):
            url = futures[future]
            done += 1
            try:
                key, title, target, text = future.result()
            except Exception as error:
                failures.append(f"{url} ({error})")
                print(f"FAIL {url} {error}", flush=True)
                continue
            path = root / target
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
            pages[key] = target
            titles[key] = title
            if done % 20 == 0 or done == len(urls):
                print(f"{done}/{len(urls)}", flush=True)

    if not args.only:
        order = sidebar(fetch(ENTRY))
        for key, title in list(order):
            if key in titles:
                order[order.index((key, title))] = (key, titles[key])
        known = {key for key, _ in order}
        for key in sorted(pages):
            if key not in known:
                order.append((key, titles.get(key, key or "index")))
        write_readme(root, pages, order)

    print(f"{len(pages)} pages, {len(failures)} failures, in {root}")
    if failures:
        print("\n".join(failures))
        sys.exit(1)


if __name__ == "__main__":
    main()
