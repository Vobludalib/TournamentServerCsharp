import os

size = 0

for dirpath, dirnames, filenames in os.walk("."):
    for file in filenames:
        if file.endswith(".cs"):
            p = os.path.join(dirpath, file)
            with open(p, "r") as f:
                if r"///Countable" in f.read(20):
                    stats = os.stat(p)
                    size = size + stats.st_size
                    print(f"File {p}: {stats.st_size / 1024 :.2g} KiB, so far at: {size / 1024 :.2g} KiB")