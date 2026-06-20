# SRTP - Simple Reliable Transport Protocol

Implementacao da Parte 1 do trabalho de Laboratorio de Redes: um protocolo de transporte confiavel simples sobre UDP, usando Stop-and-Wait.

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

O programa perguntara no terminal se deve rodar como receiver ou sender.

## Argumentos

- Modo: receiver ou sender.
- Porta base: porta `P` do receiver. O sender usa localmente `P+1`.
- Arquivo de entrada: arquivo enviado pelo sender.
- Arquivo de saida: arquivo gravado pelo receiver.
- Janela proposta: valor de 1 a 255. Nesta parte, o comportamento efetivo e Stop-and-Wait com janela 1.

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

## Protocolo

- Cabecalho fixo de 9 bytes.
- Flags `SYN`, `FIN`, `ACK` e `NACK`.
- Campos `SEQ` e `ACK` de 14 bits, com wrap-around em 16383.
- Payload maximo de 255 bytes.
- CRC32 IEEE calculado sobre cabecalho com CRC zerado mais payload.
- Three-way handshake: `SYN`, `SYN+ACK`, `ACK`.
- Transferencia confiavel por Stop-and-Wait.
- Timeout fixo de 100 ms para retransmissao.
- Encerramento com `FIN` e `FIN+ACK`.

## Limitacoes conhecidas

- Apenas o modo Stop-and-Wait (`saw`) esta implementado.
- Go-Back-N e Selective Repeat ficaram apenas previstos pela organizacao em camadas.
- O protocolo e half-duplex: em uma sessao, somente o sender envia dados de aplicacao.
- Pacotes com CRC invalido sao descartados silenciosamente, sem NACK.
