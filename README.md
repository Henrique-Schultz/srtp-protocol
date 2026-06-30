# SRTP - Simple Reliable Transport Protocol

Implementacao do trabalho de Laboratorio de Redes: um protocolo de transporte confiavel simples sobre UDP, usando Stop-and-Wait, Go-Back-N e Selective Repeat.

## Requisitos

- .NET 10.0 SDK
- Windows, Linux ou macOS

## Como compilar

```bash
dotnet build
```

## Argumentos de Linha de Comando

A execução é inteiramente controlada via argumentos de linha de comando (`CLI`). O mesmo binário suporta os modos sender, receiver e os três protocolos.

| Argumento    | Descrição                                                                                   | Obrigatório |
| :----------- | :------------------------------------------------------------------------------------------ | :---------- |
| `--mode`     | Modo de operação: `sender` ou `receiver`.                                                   | Sim         |
| `--protocol` | Protocolo a ser utilizado: `saw` (Stop-and-Wait), `gbn` (Go-Back-N) ou `sr` (Selective Repeat). | Sim         |
| `--port`     | Porta base `P`. O receiver escuta em `P` e o sender conecta nesta porta (usando `P+1` para retorno). | Sim         |
| `--host`     | Endereço IP do receiver. **(Apenas para o Sender)**                                         | Sim (Sender)|
| `--window`   | Tamanho da janela (1 a 255). Para SAW, será sempre forçado para 1.                          | Sim         |
| `--input`    | Caminho do arquivo a ser transferido. **(Apenas para o Sender)**                            | Sim (Sender)|
| `--output`   | Caminho do arquivo a ser salvo. **(Apenas para o Receiver)**                                | Sim (Receiver)|

## Exemplos de Execução (Teste Local)

Para gerar um arquivo de teste com mais de 50 pacotes:

```powershell
$bytes = New-Object byte[] 20000
[System.Random]::new().NextBytes($bytes)
[IO.File]::WriteAllBytes("entrada.bin", $bytes)
```

Abra dois terminais na pasta do projeto. Sempre inicie o **Receiver** primeiro.

### 1. Stop-and-Wait (SAW)
**Terminal 1 (Receiver):**
```bash
dotnet run -- --mode receiver --protocol saw --port 6000 --window 1 --output recebido_saw.bin
```
**Terminal 2 (Sender):**
```bash
dotnet run -- --mode sender --protocol saw --port 6000 --host 127.0.0.1 --window 1 --input entrada.bin
```

### 2. Go-Back-N (GBN)
**Terminal 1 (Receiver):**
```bash
dotnet run -- --mode receiver --protocol gbn --port 6000 --window 16 --output recebido_gbn.bin
```
**Terminal 2 (Sender):**
```bash
dotnet run -- --mode sender --protocol gbn --port 6000 --host 127.0.0.1 --window 16 --input entrada.bin
```

### 3. Selective Repeat (SR)
**Terminal 1 (Receiver):**
```bash
dotnet run -- --mode receiver --protocol sr --port 6000 --window 16 --output recebido_sr.bin
```
**Terminal 2 (Sender):**
```bash
dotnet run -- --mode sender --protocol sr --port 6000 --host 127.0.0.1 --window 16 --input entrada.bin
```

Verificação de integridade via hash no PowerShell:

```powershell
Get-FileHash .\entrada.bin -Algorithm SHA256
Get-FileHash .\recebido_sr.bin -Algorithm SHA256
```

Os hashes devem ser idênticos.

## Protocolo

- Cabecalho fixo de 9 bytes.
- Flags `SYN`, `FIN`, `ACK` e `NACK`.
- Campos `SEQ` e `ACK` de 14 bits, com wrap-around em 16383.
- Payload maximo de 255 bytes.
- CRC32 IEEE calculado sobre cabecalho com CRC zerado mais payload.
- Three-way handshake: `SYN`, `SYN+ACK`, `ACK`.
- Transferencia confiavel por Stop-and-Wait, Go-Back-N ou Selective Repeat.
- Timeout fixo de 100 ms para retransmissao.
- Encerramento com `FIN` e `FIN+ACK`.

