"""Package project source only; never include game installations, build caches or saves."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED

root = Path(__file__).resolve().parents[1]
destination = root / "dist" / "CS2Craft-source-prototype.zip"
excluded = {"build", "bin", "obj", "run", ".gradle", ".git", "__pycache__"}
entries = ["README.md", "LICENSE", "THIRD-PARTY-NOTICES.md", ".gitignore",
           "Directory.Build.props", "NuGet.Config", "CS2Craft.slnx", "docs", "minecraft", "native",
           "src", "tests", "third_party", "tools"]
destination.parent.mkdir(exist_ok=True)
with ZipFile(destination, "w", ZIP_DEFLATED) as archive:
    for entry in entries:
        path = root / entry
        candidates = sorted(path.rglob("*")) if path.is_dir() else [path]
        for file in candidates:
            relative = file.relative_to(root)
            if file.is_file() and not file.is_symlink() and not excluded.intersection(relative.parts):
                archive.write(file, relative.as_posix())
print(destination)
