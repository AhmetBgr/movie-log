import re
with open(r"e:\Works\movie-log\Pages\Franchise.razor", "r", encoding="utf-8") as f:
    text = f.read()

# Replace the banner
text = re.sub(
    r'<div class="franchise-header position-relative bg-dark" style="height: 35vh; min-height: 250px;">',
    r'<div class="franchise-header position-relative bg-dark mx-auto mt-3 rounded-4 overflow-hidden shadow" style="width: 70%; height: 20vh; min-height: 150px;">',
    text
)

text = re.sub(
    r'style="width: 120px; height: 180px;',
    r'style="width: 80px; height: 120px;',
    text
)

text = re.sub(
    r'<h1 class="fw-black mb-2 text-shadow" style="letter-spacing: -0.02em;">',
    r'<h2 class="fw-black mb-2 text-shadow" style="letter-spacing: -0.02em; font-size: 1.5rem;">',
    text
)
text = re.sub(r'</h1>', r'</h2>', text, count=1)

# Replace the grid sizes to make cards 30% smaller (more per row)
text = re.sub(
    r'<div class="col-12 col-md-6 col-lg-4 col-xl-3">',
    r'<div class="col-6 col-sm-4 col-md-3 col-lg-2 col-xl-2 px-2">',
    text
)

# And reduce the padding on the page content to fit the new width
text = re.sub(
    r'<div class="page-content bg-light p-4 p-md-5" style="overflow-y: auto; height: 65vh;">',
    r'<div class="page-content bg-light p-3 p-md-4 mt-3" style="overflow-y: auto; height: 70vh;">',
    text
)

# Reduce font sizes on the cards
text = re.sub(
    r'font-size: 1.05rem;',
    r'font-size: 0.85rem;',
    text
)
text = re.sub(
    r'font-size: 0.8rem;',
    r'font-size: 0.65rem;',
    text
)
with open(r"e:\Works\movie-log\Pages\Franchise.razor", "w", encoding="utf-8") as f:
    f.write(text)
print("Updated Franchise.razor")
