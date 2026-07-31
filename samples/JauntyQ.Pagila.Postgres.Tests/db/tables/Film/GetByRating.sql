select film_id, title, release_year, rating, length
from film
where rating = @Rating
order by film_id
