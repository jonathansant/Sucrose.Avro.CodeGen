# Sucrose.Avro.CodeGen

### Installation

`dotnet tool install Sucrose.Avro.CodeGen -g`

### Usage

```
avromagic \
--registry-url https://xxx.dev/schema-registry \
--subject-pattern user-.* \
--output-dir ./autogen \
--namespace-mapping com.somcompany.user:SomeCompany.User
```
