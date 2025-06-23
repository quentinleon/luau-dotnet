export type Alpha = {
    value: number,
}

local module = {}

module.new = function(value: number) 
    local data: Alpha = {
        value = value,
        __tostring = function(self): string
            return `Alpha:{self.value}`
        end
    }

    return data
end

return module