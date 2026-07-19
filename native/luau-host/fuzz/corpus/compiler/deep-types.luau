export type Node<T> = { value: T, next: Node<{ T }>?, visit: <U>(U, (T) -> U) -> U }
local root: Node<number>? = nil
return root
