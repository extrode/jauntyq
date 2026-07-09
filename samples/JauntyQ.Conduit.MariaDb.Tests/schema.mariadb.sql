-- Conduit/RealWorld data-layer smoke test (torture test, Part 3). Seven
-- tables covering the public RealWorld API spec's domain (users, follows,
-- articles, tags, article_tags, favorites, comments) -- see
-- docs/torture-test-log.md "Part 3 kickoff scope decisions" for why this
-- was built from the spec rather than a cloned third-party repo.
--
-- `follows`/`article_tags`/`favorites` are composite-PK junction tables --
-- the first use of a multi-column primary key in this repo's samples.
-- `password_hash` holds a SHA-256 hex digest (no JWT/session machinery is
-- in scope here at the data layer).
-- created_at/updated_at are ISO-8601 UTC strings stored in VARCHAR(64)
-- columns (NOT native DATETIME): the C# domain records (ArticleView.CreatedAt,
-- CommentView.CreatedAt, etc.) are typed `string`, and Program.cs writes
-- DateTime.UtcNow.ToString("O") directly as a string parameter, so every
-- dialect stores these as plain string columns to keep behavior identical.
--
-- Foreign keys use ON DELETE CASCADE so deleting an article correctly
-- removes its article_tags/favorites/comments rows. Every FK is authored as
-- an out-of-line FOREIGN KEY constraint -- inline column-level REFERENCES is
-- silently dropped by MySQL/MariaDB (known finding from Part 2). Unlike SQL
-- Server, MariaDB has no "multiple cascade paths" restriction, so the `follows`
-- table's two ON DELETE CASCADE FKs to users(id) (follower_id and
-- followed_id) are kept as-is -- nothing in this API ever deletes a user row,
-- so the cascade is never exercised in practice.

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

-- All seeded users share the same known test password ("Password123!") so
-- UserTests can exercise a real hash-compare login path.
INSERT INTO users (username, email, password_hash, bio, image) VALUES
    ('jane', 'jane@example.com', 'a109e36947ad56de1dca1cc49f0ef8ac9ad9a7b1aa0df41fb3c4cb73c1ff01ea', 'Jane''s bio', NULL),
    ('bob',  'bob@example.com',  'a109e36947ad56de1dca1cc49f0ef8ac9ad9a7b1aa0df41fb3c4cb73c1ff01ea', 'Bob''s bio',  NULL),
    ('carol','carol@example.com','a109e36947ad56de1dca1cc49f0ef8ac9ad9a7b1aa0df41fb3c4cb73c1ff01ea', 'Carol''s bio',NULL),
    ('dave', 'dave@example.com', 'a109e36947ad56de1dca1cc49f0ef8ac9ad9a7b1aa0df41fb3c4cb73c1ff01ea', 'Dave''s bio', NULL);

-- jane follows bob and carol, but not dave -- used to prove the feed query
-- excludes dave's articles.
INSERT INTO follows (follower_id, followed_id) VALUES
    (1, 2),
    (1, 3);

INSERT INTO tags (name) VALUES
    ('dotnet'), ('sql'), ('sqlite'), ('database'), ('offtopic');

INSERT INTO articles (slug, title, description, body, author_id, created_at, updated_at) VALUES
    ('intro-to-jauntyq',    'Intro to JauntyQ',    'A first look',        'Body 1', 2, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z'),
    ('sqlite-tips',         'SQLite Tips',         'Tips and tricks',     'Body 2', 3, '2026-01-02T00:00:00Z', '2026-01-02T00:00:00Z'),
    ('composite-keys-101',  'Composite Keys 101',  'Junction tables',     'Body 3', 2, '2026-01-03T00:00:00Z', '2026-01-03T00:00:00Z'),
    ('unrelated-post',      'Unrelated Post',      'Not about JauntyQ',   'Body 4', 4, '2026-01-04T00:00:00Z', '2026-01-04T00:00:00Z');

-- tag ids: dotnet=1, sql=2, sqlite=3, database=4, offtopic=5
-- article ids: intro-to-jauntyq=1, sqlite-tips=2, composite-keys-101=3, unrelated-post=4
INSERT INTO article_tags (article_id, tag_id) VALUES
    (1, 1), (1, 2),
    (2, 3), (2, 2),
    (3, 2), (3, 4),
    (4, 5);

-- jane favorites both of bob's articles' companion (intro-to-jauntyq) and
-- carol's sqlite-tips; carol also favorites bob's intro-to-jauntyq, giving
-- it a favorites count of 2 for multi-user count correctness.
INSERT INTO favorites (user_id, article_id) VALUES
    (1, 1),
    (1, 2),
    (3, 1);

INSERT INTO comments (article_id, author_id, body, created_at, updated_at) VALUES
    (1, 1, 'Great read, jane here.', '2026-01-05T00:00:00Z', '2026-01-05T00:00:00Z'),
    (1, 3, 'Nice one, carol here.',  '2026-01-06T00:00:00Z', '2026-01-06T00:00:00Z');
