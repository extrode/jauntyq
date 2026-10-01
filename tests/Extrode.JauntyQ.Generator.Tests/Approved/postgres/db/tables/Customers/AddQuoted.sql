-- @identity
insert into customers (code, name, balance, is_active, created_at, visits) -- the row
/* values come
   next */ values (@Code, @Name, @Balance, @IsActive, @CreatedAt, @Visits);
