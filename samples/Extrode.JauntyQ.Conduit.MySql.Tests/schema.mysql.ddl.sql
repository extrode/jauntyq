-- DDL-only counterpart to schema.mysql.sql, for fixtures that must start
-- from an empty database. schema.mysql.sql's seed INSERTs (jane/bob/carol/
-- dave, sample articles, etc.) are fixture data for the direct-repository
-- tests (ConduitMySqlFixture) -- HTTP-level black-box tests instead create
-- every user/article they need through the API itself, and would otherwise
-- collide with those seeded usernames/emails (e.g. registering "bob" or
-- "dave" via POST /api/users would 422 against the pre-seeded row).
--
-- See schema.mysql.sql for the notes on VARCHAR date-string columns, the
-- out-of-line FOREIGN KEY authoring (inline REFERENCES is silently dropped by
-- MySQL/MariaDB), and the `follows` ON DELETE CASCADE (kept as-is on MySQL,
-- which has no SQL Server multiple-cascade-path restriction).

CREATE TABLE users (
    id             INT AUTO_INCREMENT PRIMARY KEY,
    username       VARCHAR(255) NOT NULL UNIQUE,
    email          VARCHAR(255) NOT NULL UNIQUE,
    password_hash  TEXT NOT NULL,
    bio            TEXT NOT NULL,
    image          VARCHAR(255) NULL
);

CREATE TABLE follows (
    follower_id  INT NOT NULL,
    followed_id  INT NOT NULL,
    PRIMARY KEY (follower_id, followed_id),
    FOREIGN KEY (follower_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (followed_id) REFERENCES users(id) ON DELETE CASCADE
);

CREATE TABLE articles (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    slug         VARCHAR(255) NOT NULL UNIQUE,
    title        TEXT NOT NULL,
    description  TEXT NOT NULL,
    body         TEXT NOT NULL,
    author_id    INT NOT NULL,
    created_at   VARCHAR(64) NOT NULL,
    updated_at   VARCHAR(64) NOT NULL,
    FOREIGN KEY (author_id) REFERENCES users(id)
);

CREATE TABLE tags (
    id    INT AUTO_INCREMENT PRIMARY KEY,
    name  VARCHAR(255) NOT NULL UNIQUE
);

CREATE TABLE article_tags (
    article_id  INT NOT NULL,
    tag_id      INT NOT NULL,
    PRIMARY KEY (article_id, tag_id),
    FOREIGN KEY (article_id) REFERENCES articles(id) ON DELETE CASCADE,
    FOREIGN KEY (tag_id) REFERENCES tags(id)
);

CREATE TABLE favorites (
    user_id     INT NOT NULL,
    article_id  INT NOT NULL,
    PRIMARY KEY (user_id, article_id),
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    FOREIGN KEY (article_id) REFERENCES articles(id) ON DELETE CASCADE
);

CREATE TABLE comments (
    id           INT AUTO_INCREMENT PRIMARY KEY,
    article_id   INT NOT NULL,
    author_id    INT NOT NULL,
    body         TEXT NOT NULL,
    created_at   VARCHAR(64) NOT NULL,
    updated_at   VARCHAR(64) NOT NULL,
    FOREIGN KEY (article_id) REFERENCES articles(id) ON DELETE CASCADE,
    FOREIGN KEY (author_id) REFERENCES users(id)
);

-- Added in response to JauntyQ's JNT8004 index-advisor warning on
-- Comments/GetByArticleId.sql's `where article_id = @ArticleId` filter.
CREATE INDEX idx_comments_article_id ON comments (article_id);
CREATE INDEX idx_comments_author_id ON comments (author_id);
CREATE INDEX idx_comments_created_at ON comments (created_at);
CREATE INDEX idx_articles_author_id ON articles (author_id);
CREATE INDEX idx_articles_created_at ON articles (created_at);
