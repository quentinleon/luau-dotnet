local Person = {}
Person.__tostring = function(self: Person)
    return string.format("Person { name=\"%s\", age=%d }", self.name, self.age)
end

export type PersonData = {
    name: string,
    age: number,
}

export type Person = typeof(setmetatable({} :: PersonData, Person))

function Person.new(name: string, age: number): Person
    local person: PersonData = {
        name = name,
        age = age,
    }

    return setmetatable(person, Person);
end

print("loaded")
return Person