local test = require "./person"

local p = test.new("Alice", 18)
print(p)

local co = coroutine.create(function()
    for i = 1, 10 do
        wait(0.5)
        coroutine.yield(i)
    end
end)

return co