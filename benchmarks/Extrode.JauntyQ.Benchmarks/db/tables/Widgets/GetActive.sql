select WidgetId, Name, Price, Quantity, Active
from Widgets
where Active = @Active
