-- @type actor_id int
select distinct a2.actor_id as actor_id, a2.first_name, a2.last_name
from film_actor fa1
join film_actor fa2 on fa2.film_id = fa1.film_id and fa2.actor_id <> fa1.actor_id
join actor a2 on a2.actor_id = fa2.actor_id
where fa1.actor_id = @ActorId
order by a2.last_name, a2.first_name
