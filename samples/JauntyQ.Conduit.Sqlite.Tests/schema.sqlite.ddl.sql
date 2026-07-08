-- DDL-only counterpart to schema.sqlite.sql, for fixtures that must start
-- from an empty database. schema.sqlite.sql's seed INSERTs (jane/bob/carol/
-- dave, sample articles, etc.) are fixture data for the direct-repository
-- tests (ConduitSqliteFixture) -- HTTP-level black-box tests instead create
-- every user/article they need through the API itself, and would otherwise
-- collide with those seeded usernames/emails (e.g. registering "bob" or
-- "dave" via POST /api/users would 422 against the pre-seeded row).

CREATE TABLE users (
    id             INTEGER NOT NULL PRIMARY KEY,
    username       TEXT NOT NULL UNIQUE,
    email          TEXT NOT NULL UNIQUE,
    password_hash  TEXT NOT NULL,
    bio            TEXT NOT NULL,
    image          TEXT NULL
);

CREATE TABLE follows (
    follower_id  INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    followed_id  INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    PRIMARY KEY (follower_id, followed_id)
);

CREATE TABLE articles (
    id           INTEGER NOT NULL PRIMARY KEY,
    slug         TEXT NOT NULL UNIQUE,
    title        TEXT NOT NULL,
    description  TEXT NOT NULL,
    body         TEXT NOT NULL,
    author_id    INTEGER NOT NULL REFERENCES users(id),
    created_at   TEXT NOT NULL,
    updated_at   TEXT NOT NULL
);

CREATE TABLE tags (
    id    INTEGER NOT NULL PRIMARY KEY,
    name  TEXT NOT NULL UNIQUE
);

CREATE TABLE article_tags (
    article_id  INTEGER NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    tag_id      INTEGER NOT NULL REFERENCES tags(id),
    PRIMARY KEY (article_id, tag_id)
);

CREATE TABLE favorites (
    user_id     INTEGER NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    article_id  INTEGER NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, article_id)
);

CREATE TABLE comments (
    id           INTEGER NOT NULL PRIMARY KEY,
    article_id   INTEGER NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    author_id    INTEGER NOT NULL REFERENCES users(id),
    body         TEXT NOT NULL,
    created_at   TEXT NOT NULL,
    updated_at   TEXT NOT NULL
);

-- Added in response to JauntyQ's JNT8004 index-advisor warning on
-- Comments/GetByArticleId.sql's `where article_id = @ArticleId` filter.
CREATE INDEX idx_comments_article_id ON comments (article_id);
