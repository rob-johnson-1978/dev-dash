from flask import Blueprint, jsonify, request

from db import create_author_with_books, fetch_authors_with_books

bp = Blueprint("authors", __name__)


@bp.route("/authors", methods=["GET", "POST"])
def authors() -> tuple:
    if request.method == "GET":
        return _get_authors()
    if request.method == "POST":
        return _create_author()
    return ("Method not allowed", 405)


def _get_authors() -> tuple:
    try:
        authors = fetch_authors_with_books()
        if authors is None:
            authors = []
        return jsonify(authors), 200
    except Exception as exc:  # pragma: no cover - logged at server
        print(f"Error querying authors: {exc}")
        return ("Internal server error", 500)


def _create_author() -> tuple:
    try:
        payload = request.get_json(silent=True)
        if not payload:
            return ("Bad request", 400)

        name = payload.get("name")
        email = payload.get("email")
        books = payload.get("books") or []

        author = create_author_with_books(name, email, books)
        return jsonify(author), 201
    except Exception as exc:  # pragma: no cover - logged at server
        print(f"Error creating author: {exc}")
        return ("Internal server error", 500)


def register_routes(app) -> None:
    app.register_blueprint(bp)
