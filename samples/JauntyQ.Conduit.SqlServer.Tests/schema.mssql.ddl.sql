-- DDL-only counterpart to schema.mssql.sql, for fixtures that must start
-- from an empty database. schema.mssql.sql's seed INSERTs (jane/bob/carol/
-- dave, sample articles, etc.) are fixture data for the direct-repository
-- tests (ConduitSqlServerFixture) -- HTTP-level black-box tests instead create
-- every user/article they need through the API itself, and would otherwise
-- collide with those seeded usernames/emails (e.g. registering "bob" or
-- "dave" via POST /api/users would 422 against the pre-seeded row).
--
-- See schema.mssql.sql for the notes on VARCHAR(64) date-string columns and
-- the `follows` ON DELETE CASCADE omission (SQL Server error 1785).

CREATE TABLE users (
    id             INT IDENTITY(1,1) PRIMARY KEY,
    username       VARCHAR(255) NOT NULL UNIQUE,
    email          VARCHAR(255) NOT NULL UNIQUE,
    password_hash  VARCHAR(255) NOT NULL,
    bio            VARCHAR(MAX) NOT NULL,
    image          VARCHAR(255) NULL
);

CREATE TABLE follows (
    follower_id  INT NOT NULL REFERENCES users(id),
    followed_id  INT NOT NULL REFERENCES users(id),
    PRIMARY KEY (follower_id, followed_id)
);

CREATE TABLE articles (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    slug         VARCHAR(255) NOT NULL UNIQUE,
    title        VARCHAR(MAX) NOT NULL,
    description  VARCHAR(MAX) NOT NULL,
    body         VARCHAR(MAX) NOT NULL,
    author_id    INT NOT NULL REFERENCES users(id),
    created_at   VARCHAR(64) NOT NULL,
    updated_at   VARCHAR(64) NOT NULL
);

CREATE TABLE tags (
    id    INT IDENTITY(1,1) PRIMARY KEY,
    name  VARCHAR(255) NOT NULL UNIQUE
);

CREATE TABLE article_tags (
    article_id  INT NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    tag_id      INT NOT NULL REFERENCES tags(id),
    PRIMARY KEY (article_id, tag_id)
);

CREATE TABLE favorites (
    user_id     INT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    article_id  INT NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    PRIMARY KEY (user_id, article_id)
);

CREATE TABLE comments (
    id           INT IDENTITY(1,1) PRIMARY KEY,
    article_id   INT NOT NULL REFERENCES articles(id) ON DELETE CASCADE,
    author_id    INT NOT NULL REFERENCES users(id),
    body         VARCHAR(MAX) NOT NULL,
    created_at   VARCHAR(64) NOT NULL,
    updated_at   VARCHAR(64) NOT NULL
);

-- Added in response to JauntyQ's JNT8004 index-advisor warning on
-- Comments/GetByArticleId.sql's `where article_id = @ArticleId` filter.
CREATE INDEX idx_comments_article_id ON comments (article_id);
CREATE INDEX idx_comments_author_id ON comments (author_id);
CREATE INDEX idx_comments_created_at ON comments (created_at);
CREATE INDEX idx_articles_author_id ON articles (author_id);
CREATE INDEX idx_articles_created_at ON articles (created_at);
