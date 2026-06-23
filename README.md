# SRTP - Simple Reliable Transport Protocol

Implementacao do trabalho de Laboratorio de Redes: um protocolo de transporte confiavel simples sobre UDP, usando Stop-and-Wait, Go-Back-N e Selective Repeat.

## Requisitos

- .NET 8 ou superior
- Windows, Linux ou macOS com suporte ao SDK do .NET

O projeto usa `net8.0` e foi validado com SDK .NET 9, que compila projetos .NET 8.

## Como compilar

```bash
dotnet build
```

## Como executar

Execute:

```bash
dotnet run
```

O programa perguntara no terminal se deve rodar como receiver ou sender e qual protocolo deve usar.

## Argumentos

- Modo: receiver ou sender.
- Protocolo: Stop-and-Wait, Go-Back-N ou Selective Repeat.
- Porta base: porta `P` do receiver. O sender usa localmente `P+1`.
- Arquivo de entrada: arquivo enviado pelo sender.
- Arquivo de saida: arquivo gravado pelo receiver.
- Janela proposta: valor de 1 a 255. No Stop-and-Wait, a janela efetiva e 1. No Go-Back-N e no Selective Repeat, a janela controla quantos pacotes podem ficar em voo.

## Teste local

Em um terminal:

```bash
dotnet run
```

Em outro terminal:

```bash
dotnet run
```

Para gerar um arquivo de teste com mais de 50 pacotes:

```powershell
$bytes = New-Object byte[] 20000
[System.Random]::new().NextBytes($bytes)
[IO.File]::WriteAllBytes("entrada.bin", $bytes)
```

Verificacao por hash no PowerShell:

```powershell
Get-FileHash .\entrada.bin -Algorithm SHA256
Get-FileHash .\recebido.bin -Algorithm SHA256
```

Os hashes devem ser identicos.

### Entradas para testar cada protocolo

Use dois terminais. Primeiro rode o receiver, depois rode o sender.

Stop-and-Wait:

```text
Receiver: 1, 1, 5000, 1, recebido-saw.bin
Sender:   2, 1, 5000, 1, 127.0.0.1, entrada.bin
```

Go-Back-N:

```text
Receiver: 1, 2, 5000, 4, recebido-gbn.bin
Sender:   2, 2, 5000, 4, 127.0.0.1, entrada.bin
```

Selective Repeat:

```text
Receiver: 1, 3, 5000, 4, recebido-sr.bin
Sender:   2, 3, 5000, 4, 127.0.0.1, entrada.bin
```

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

