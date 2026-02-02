import atexit
import os

from flask import Flask

from db import close_pool, init_database
from handlers import register_routes


def create_app() -> Flask:
    init_database()

    app = Flask(__name__)
    register_routes(app)
    return app


app = create_app()


@atexit.register
def _shutdown() -> None:
    close_pool()


if __name__ == "__main__":
    port = int(os.getenv("PORT", "8082"))
    app.run(host="0.0.0.0", port=port)
