select
    e.employee_id,
    e.first_name,
    e.last_name,
    m.first_name as manager_first_name,
    m.last_name as manager_last_name
from employees e
left join employees m on e.manager_id = m.employee_id
