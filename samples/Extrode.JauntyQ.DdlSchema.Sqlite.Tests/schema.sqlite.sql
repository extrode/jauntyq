CREATE TABLE Widgets (
    WidgetId  INTEGER PRIMARY KEY,
    Name      TEXT NOT NULL,
    Quantity  INTEGER NOT NULL
);

INSERT INTO Widgets (Name, Quantity) VALUES ('Sprocket', 10), ('Cog', 20);
