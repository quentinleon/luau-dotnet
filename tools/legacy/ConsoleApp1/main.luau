local test = require "@src/person"

local p = test.new("Alice", 18)
print(p)

cmd.Hello()
cmd.echo("Hello, luau!")
print(cmd.foo)
cmd.foo = 10
print(cmd.foo)

local co = coroutine.create(function()
    for i = 1, 10 do
        wait(0.5)
        coroutine.yield(i)
    end
end)

return co