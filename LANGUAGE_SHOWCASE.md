# Kong Language Tour

Super-brief examples of the core language.

## 1) Variables, types, comments, operators

```text
fn Main() {
    // type inference
    let x = 41

    // type annotations
    let y: int = 1
    let ok: bool = true
    let text: string = "hello"

    // operators
    let sum = x + y
    let check = (sum == 42) && ok
}
```

## 2) Control flow: if/else + for-in

```text
fn Main() {
    let nums = [1, 2, 3, 4]
    var total: int = 0

    for n in nums {
        total = total + n
    }

    let label = if (total > 5) { "big" } else { "small" }
}
```

## 3) Functions, first-class functions, closures/lambdas

```text
fn Add(a: int, b: int) -> int {
    a + b
}

fn MakeAdder(delta: int) -> fn(int) -> int {
    fn(x: int) -> int {
        x + delta
    }
}

fn Main() {
    let addOne: fn(int) -> int = MakeAdder(1) // first-class function value
    let n = addOne(Add(40, 1))
}
```

## 4) Classes, interfaces, generics

```text
interface IGet<T> {
    fn Get(self) -> T
}

class Box<T> {
    value: T
}

impl Box {
    init(value: T) {
        self.value = value
    }

    fn Get(self) -> T {
        self.value
    }
}

impl IGet for Box {
    fn Get(self) -> T {
        self.value
    }
}

fn Main() {
    let box: Box<int> = new Box<int>(42)
    let getter: IGet<int> = box
    let value = getter.Get()
}
```

## 5) Enums + match

```text
enum Result<T, E> {
    Ok(T),
    Err(E),
}

fn Main() {
    let r: Result<int, string> = Ok(42)

    let text = match (r) {
        Ok(v) => { "value=" + v.ToString() },
        Err(e) => { "error=" + e },
    }
}
```

## 6) BCL interop

```text
use System
use System.IO

module KongDemo

fn Main() {
    let cwd = Environment.CurrentDirectory // static property
    let hasReadme = File.Exists(cwd + "/README.md") // static method
    Console.WriteLine(hasReadme) // static method
}
```
